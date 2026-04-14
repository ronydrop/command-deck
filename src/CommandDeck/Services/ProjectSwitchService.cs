using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommandDeck.Models;
using CommandDeck.ViewModels;
using Debug = System.Diagnostics.Trace;

namespace CommandDeck.Services;

/// <summary>
/// Implements the full project-switch use case, extracted from MainViewModel.
/// ViewModels are injected as <see cref="Lazy{T}"/> to prevent circular
/// DI dependencies (services should not directly reference ViewModels;
/// Lazy defers resolution until first use).
/// </summary>
public class ProjectSwitchService : IProjectSwitchService
{
    private readonly ISettingsService _settingsService;
    private readonly IWorkspaceService _workspaceService;
    private readonly INotificationService _notificationService;
    private readonly CanvasItemFactory _canvasItemFactory;
    private readonly IEventBusService _eventBus;
    private readonly ITileContextService _tileContext;
    private readonly IActivityFeedService _activityFeed;

    // ViewModels injected lazily to break circular DI chains.
    private readonly Lazy<TerminalCanvasViewModel> _canvasVm;
    private readonly Lazy<DashboardViewModel> _dashboardVm;
    private readonly Lazy<ProjectListViewModel> _projectListVm;

    /// <inheritdoc/>
    public event EventHandler<ProjectSwitchResult>? SwitchCompleted;

    public ProjectSwitchService(
        ISettingsService settingsService,
        IWorkspaceService workspaceService,
        INotificationService notificationService,
        CanvasItemFactory canvasItemFactory,
        IEventBusService eventBus,
        ITileContextService tileContext,
        IActivityFeedService activityFeed,
        Lazy<TerminalCanvasViewModel> canvasVm,
        Lazy<DashboardViewModel> dashboardVm,
        Lazy<ProjectListViewModel> projectListVm)
    {
        _settingsService = settingsService;
        _workspaceService = workspaceService;
        _notificationService = notificationService;
        _canvasItemFactory = canvasItemFactory;
        _eventBus = eventBus;
        _tileContext = tileContext;
        _activityFeed = activityFeed;
        _canvasVm = canvasVm;
        _dashboardVm = dashboardVm;
        _projectListVm = projectListVm;
    }

    /// <inheritdoc/>
    public async Task SwitchToAsync(
        Project project,
        ProjectSwitchContext context,
        IProgress<string>? progress = null)
    {
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();

        var restoredTerminals = new List<TerminalViewModel>();
        TerminalViewModel? activeTerminal = null;
        string? gitBranchDisplay = null;

        try
        {
            progress?.Report($"Carregando {project.Name}…");

            // 1. Save current layout
            if (context.ActiveTerminals.Count > 0 || context.CurrentProjectId is not null)
                await _canvasVm.Value.SaveCurrentLayoutAsync();

            Debug.WriteLine($"[Perf] SaveLayout: {stepSw.ElapsedMilliseconds}ms");
            stepSw.Restart();

            // 2. Dispose active terminals
            progress?.Report($"Fechando terminais…");

            var disposeTasks = new List<Task>();
            foreach (var t in context.ActiveTerminals)
                disposeTasks.Add(t.DisposeAsync().AsTask());

            await Task.WhenAll(disposeTasks);
            Debug.WriteLine($"[Perf] DisposeTerminals: {stepSw.ElapsedMilliseconds}ms");
            stepSw.Restart();

            // 3. Clear canvas
            _workspaceService.ClearAll();

            // 4. Update canvas view-model with new project id
            _canvasVm.Value.CurrentProjectId = project.Id;

            // 5. Sync project-list sidebar selection
            var filteredMatch = _projectListVm.Value.FilteredProjects
                .FirstOrDefault(p => p.Id == project.Id) ?? project;
            _projectListVm.Value.SelectedProject = filteredMatch;

            // 6. Persist settings, fetch git info & switch browser session in parallel
            progress?.Report($"Carregando informações do projeto…");

            var settingsTask = Task.Run(async () =>
            {
                var settings = await _settingsService.GetSettingsAsync();
                settings.LastOpenedProjectId = project.Id;
                await _settingsService.SaveSettingsAsync(settings);
            });

            var dashboardTask = _dashboardVm.Value.SetProjectAsync(project);

            await Task.WhenAll(settingsTask, dashboardTask);
            Debug.WriteLine($"[Perf] ParallelTasks (settings+dashboard): {stepSw.ElapsedMilliseconds}ms");
            stepSw.Restart();

            _dashboardVm.Value.StopRefresh();

            gitBranchDisplay = _dashboardVm.Value.GitInfo?.BranchDisplay ?? string.Empty;

            var gitInfo = _dashboardVm.Value.GitInfo;
            _notificationService.Notify(
                $"Projeto '{project.Name}' carregado",
                NotificationType.Success,
                NotificationSource.System,
                message: gitInfo != null ? $"Branch: {gitInfo.Branch}" : null);

            // 7. Load target project layout and restore terminals
            progress?.Report($"Restaurando layout…");

            var saved = await _canvasVm.Value.LoadLayoutAsync(project.Id);
            if (saved?.Items is { Count: > 0 })
            {
                // Restore camera position
                if (saved.Camera is not null)
                    _canvasVm.Value.SyncCamera(
                        saved.Camera.OffsetX,
                        saved.Camera.OffsetY,
                        saved.Camera.Zoom);

                var terminalItems = saved.Items
                    .Where(i => i.Type == CanvasItemType.Terminal)
                    .ToList();

                progress?.Report($"Restaurando {terminalItems.Count} terminais…");

                foreach (var itemModel in terminalItems)
                {
                    var shellType = itemModel.Metadata.TryGetValue("shellType", out var st)
                        && Enum.TryParse<ShellType>(st, true, out var parsed)
                            ? parsed
                            : project.DefaultShell;

                    var workDir = itemModel.Metadata.TryGetValue("workingDirectory", out var wd)
                        ? wd
                        : project.Path;

                    var terminalVm = context.TerminalVmFactory();
                    restoredTerminals.Add(terminalVm);

                    // PrepareAsync MUST complete before AddRestoredItem so that when WPF
                    // creates the TerminalControl and OnLoaded fires, StartSessionAsync finds
                    // the correct shell type and working directory already set on the ViewModel.
                    await terminalVm.PrepareAsync(shellType, workDir, project.Id);

                    var canvasItem = _canvasItemFactory.CreateTerminalItemFromModel(terminalVm, itemModel);
                    _workspaceService.AddRestoredItem(canvasItem);

                    // Yield at Render priority so the dispatcher can paint the spinner
                    // frame before we create the next terminal card.
                    await Application.Current.Dispatcher.InvokeAsync(
                        static () => { }, DispatcherPriority.Render);
                }

                Debug.WriteLine(
                    $"[Perf] RestoreTerminals ({terminalItems.Count}): {stepSw.ElapsedMilliseconds}ms");

                activeTerminal = restoredTerminals.FirstOrDefault();

                // Restore non-terminal canvas items (Chat, CodeEditor, FileExplorer, Widgets)
                var nonTerminalItems = saved.Items
                    .Where(i => i.Type != CanvasItemType.Terminal)
                    .ToList();

                foreach (var itemModel in nonTerminalItems)
                {
                    CanvasItemViewModel? restored = itemModel.Type switch
                    {
                        CanvasItemType.ChatWidget =>
                            _canvasItemFactory.CreateChatTileItemFromModel(itemModel),
                        CanvasItemType.CodeEditorWidget =>
                            _canvasItemFactory.CreateCodeEditorItemFromModel(itemModel),
                        CanvasItemType.FileExplorerWidget =>
                            _canvasItemFactory.CreateFileExplorerItemFromModel(itemModel),
                        CanvasItemType.BrowserWidget =>
                            _canvasItemFactory.CreateBrowserItemFromModel(itemModel),
                        CanvasItemType.ActivityFeedWidget =>
                            _canvasItemFactory.CreateActivityFeedItemFromModel(itemModel),
                        _ => null  // WidgetCanvasItemViewModel types are handled by AddWidgetItem
                    };

                    if (restored is not null)
                        _workspaceService.AddRestoredItem(restored);
                }

                // Yield at Background priority (4) which is LOWER than Loaded (6)
                // and Render (7). By the time this runs, all TerminalControl.OnLoaded
                // events have fired, StartSessionAsync has created the ConPTY sessions,
                // and SessionCreated events have been processed. Only then is it safe
                // to recategorise projects as Active/Inactive.
                await Application.Current.Dispatcher.InvokeAsync(
                    static () => { }, DispatcherPriority.Background);
                _projectListVm.Value.RefreshActiveState();
            }

            Debug.WriteLine($"[Perf] SwitchProject TOTAL: {totalSw.ElapsedMilliseconds}ms");
            progress?.Report($"Project: {project.Name} — ready");

            SwitchCompleted?.Invoke(this, new ProjectSwitchResult
            {
                SwitchedTo = project,
                RestoredTerminals = restoredTerminals,
                ActiveTerminal = activeTerminal,
                GitBranchDisplay = gitBranchDisplay,
                Success = true
            });

            // Publish on event bus and update shared context
            _eventBus.Publish(BusEventType.Project_Switched, project, source: "ProjectSwitchService");
            _tileContext.Set(TileContextKeys.ProjectId, project.Id, sourceLabel: "Project");
            _tileContext.Set(TileContextKeys.ProjectName, project.Name, sourceLabel: "Project");
            _tileContext.Set(TileContextKeys.ProjectPath, project.Path, sourceLabel: "Project");
            _activityFeed.Log(ActivityEntryType.Project, $"Projeto aberto: {project.Name}",
                detail: project.Path, icon: "📂");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProjectSwitchService] ERROR: {ex}");

            SwitchCompleted?.Invoke(this, new ProjectSwitchResult
            {
                SwitchedTo = project,
                RestoredTerminals = restoredTerminals,
                ActiveTerminal = null,
                Success = false,
                ErrorMessage = ex.Message
            });

            throw;
        }
    }
}
