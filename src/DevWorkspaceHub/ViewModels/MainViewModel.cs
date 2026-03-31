using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// Main ViewModel for the application. Manages navigation, terminal tabs, and sidebar state.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ITerminalService _terminalService;
    private readonly IProjectService _projectService;
    private readonly IGitService _gitService;
    private readonly IProcessMonitorService _processMonitorService;
    private readonly ISettingsService _settingsService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ICommandPaletteService _commandPaletteService;
    private readonly INotificationService _notificationService;
    private readonly IPaneStateService _paneStateService;
    private readonly IUpdateService _updateService;
    private readonly Func<TerminalViewModel> _terminalVmFactory;
    private readonly Func<SettingsViewModel> _settingsVmFactory;
    private readonly Func<ProjectEditViewModel> _projectEditVmFactory;
    private readonly CanvasItemFactory _canvasItemFactory;
    private readonly IAiTerminalLauncher _aiLauncher;
    private readonly SemaphoreSlim _projectSwitchLock = new(1, 1);
    private readonly Dictionary<TerminalViewModel, System.ComponentModel.PropertyChangedEventHandler> _terminalPropertyHandlers = new();

    // ─── Sub ViewModels ──────────────────────────────────────────────────────

    public ProjectListViewModel ProjectList { get; }
    public DashboardViewModel Dashboard { get; }
    public ProcessMonitorViewModel ProcessMonitor { get; }
    public TerminalCanvasViewModel CanvasViewModel { get; }
    public CommandPaletteViewModel CommandPalette { get; }
    public WorkspaceTreeViewModel WorkspaceTree { get; }

    /// <summary>AI Assistant side-panel ViewModel.</summary>
    public AssistantPanelViewModel AssistantPanel { get; }
    public BrowserViewModel Browser { get; }

    [ObservableProperty] private bool _isCommandPaletteOpen;

    /// <summary>True when the AI assistant side-panel is open.</summary>
    [ObservableProperty] private bool _isAIPanelOpen;

    // ─── Observable Properties ───────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<TerminalViewModel> _terminals = new();

    [ObservableProperty]
    private TerminalViewModel? _activeTerminal;

    [ObservableProperty]
    private Project? _currentProject;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    /// <summary>When true the left sidebar shows only project avatars (collapsed mode).</summary>
    [ObservableProperty]
    private bool _isSidebarCollapsed;

    /// <summary>When false the right terminal list panel is hidden.</summary>
    [ObservableProperty]
    private bool _isTerminalPanelVisible = true;

    [ObservableProperty]
    private ViewType _currentView = ViewType.Terminal;

    [ObservableProperty]
    private string _statusBarText = "Ready";

    [ObservableProperty]
    private string _gitBranchDisplay = string.Empty;

    [ObservableProperty]
    private string _shellTypeDisplay = string.Empty;

    [ObservableProperty]
    private int _activeProcessCount;

    [ObservableProperty]
    private int _activeTerminalCount;

    // ─── Notification Properties ────────────────────────────────────────────

    /// <summary>Active toast notifications for UI binding.</summary>
    public ObservableCollection<NotificationItem> ActiveNotifications =>
        _notificationService.ActiveNotifications;

    /// <summary>Aggregated pane state icons for status bar display.</summary>
    [ObservableProperty]
    private string _paneStateIcons = string.Empty;

    /// <summary>True while a project switch is in progress (drives loading overlay).</summary>
    [ObservableProperty]
    private bool _isProjectSwitching;

    /// <summary>Progress message shown on the loading overlay during project switch.</summary>
    [ObservableProperty]
    private string _projectSwitchMessage = string.Empty;

    /// <summary>Count of unread notifications for badge display.</summary>
    [ObservableProperty]
    private int _unreadNotificationCount;

    /// <summary>Notification history for the popup panel.</summary>
    public ReadOnlyObservableCollection<NotificationItem> NotificationHistory =>
        _notificationService.History;

    /// <summary>Whether the notification history popup is open.</summary>
    [ObservableProperty]
    private bool _isNotificationHistoryOpen;

    [ObservableProperty]
    private bool _isProjectEditVisible;

    [ObservableProperty]
    private ProjectEditViewModel? _projectEditViewModel;

    [ObservableProperty]
    private SettingsViewModel? _settingsViewModel;

    public MainViewModel(
        ITerminalService terminalService,
        IProjectService projectService,
        IGitService gitService,
        IProcessMonitorService processMonitorService,
        ISettingsService settingsService,
        IWorkspaceService workspaceService,
        ICommandPaletteService commandPaletteService,
        INotificationService notificationService,
        IPaneStateService paneStateService,
        IUpdateService updateService,
        TerminalCanvasViewModel canvasViewModel,
        CommandPaletteViewModel commandPalette,
        WorkspaceTreeViewModel workspaceTree,
        Func<TerminalViewModel> terminalVmFactory,
        Func<SettingsViewModel> settingsVmFactory,
        Func<ProjectEditViewModel> projectEditVmFactory,
        ProjectListViewModel projectList,
        DashboardViewModel dashboard,
        ProcessMonitorViewModel processMonitor,
        AssistantPanelViewModel assistantPanel,
        BrowserViewModel browserViewModel,
        CanvasItemFactory canvasItemFactory,
        IAiTerminalLauncher aiLauncher)
    {
        _terminalService = terminalService;
        _projectService = projectService;
        _gitService = gitService;
        _processMonitorService = processMonitorService;
        _settingsService = settingsService;
        _workspaceService = workspaceService;
        _commandPaletteService = commandPaletteService;
        _notificationService = notificationService;
        _paneStateService = paneStateService;
        _updateService = updateService;
        _terminalVmFactory = terminalVmFactory;
        _settingsVmFactory = settingsVmFactory;
        _projectEditVmFactory = projectEditVmFactory;
        _canvasItemFactory = canvasItemFactory;
        _aiLauncher = aiLauncher;

        ProjectList = projectList;
        Dashboard = dashboard;
        ProcessMonitor = processMonitor;
        CanvasViewModel = canvasViewModel;
        CommandPalette = commandPalette;
        WorkspaceTree = workspaceTree;
        AssistantPanel = assistantPanel;
        Browser = browserViewModel;

        CommandPalette.CloseRequested += () => IsCommandPaletteOpen = false;

        // Wire up events
        ProjectList.ProjectSelected += OnProjectSelected;
        ProjectList.AddProjectRequested += OnAddProjectRequested;
        ProjectList.EditProjectRequested += OnEditProjectRequested;

        Dashboard.RunCommandRequested += async cmd =>
        {
            try { await RunCommandInNewTerminalAsync(cmd); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RunCommand] {ex}"); }
        };

        // Wire up workspace switch — full orchestration with terminal restoration
        WorkspaceTree.WorkspaceSwitchRequested += async workspaceId =>
        {
            try { await SwitchWorkspaceAsync(workspaceId); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[WorkspaceSwitch] {ex}"); }
        };

        // Wire up notification events
        _paneStateService.AggregatedIconsChanged += icons => PaneStateIcons = icons;
        _notificationService.NotificationAdded += _ => UnreadNotificationCount = _notificationService.UnreadCount;
        _notificationService.NotificationDismissed += _ => UnreadNotificationCount = _notificationService.UnreadCount;
    }

    [RelayCommand]
    private void OpenCommandPalette()
    {
        IsCommandPaletteOpen = true;
        CommandPalette.OpenCommand.Execute(null);
    }

    /// <summary>
    /// Toggles the AI Assistant side-panel open/closed (Ctrl+I).
    /// </summary>
    [RelayCommand]
    private void ToggleAIPanel() => IsAIPanelOpen = !IsAIPanelOpen;

    /// <summary>
    /// Dismiss a single notification by its ID.
    /// </summary>
    [RelayCommand]
    private void DismissNotification(string notificationId)
        => _notificationService.Dismiss(notificationId);

    /// <summary>
    /// Dismiss all active toasts.
    /// </summary>
    [RelayCommand]
    private void DismissAllNotifications()
        => _notificationService.DismissAll();

    /// <summary>
    /// Mark all notifications as read (resets badge count).
    /// </summary>
    [RelayCommand]
    private void MarkNotificationsAsRead()
    {
        _notificationService.MarkAllAsRead();
        UnreadNotificationCount = 0;
    }

    /// <summary>
    /// Toggle the notification history popup.
    /// </summary>
    [RelayCommand]
    private void ToggleNotificationHistory()
    {
        IsNotificationHistoryOpen = !IsNotificationHistoryOpen;
        if (IsNotificationHistoryOpen)
        {
            _notificationService.MarkAllAsRead();
            UnreadNotificationCount = 0;
        }
    }

    /// <summary>
    /// Clear all notification history.
    /// </summary>
    [RelayCommand]
    private void ClearNotificationHistory()
    {
        _notificationService.ClearHistory();
        UnreadNotificationCount = 0;
    }

    /// <summary>
    /// Initializes the application on startup.
    /// </summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        // Initialize multi-workspace system (loads or creates the active workspace)
        await _workspaceService.InitializeAsync();

        await ProjectList.LoadProjectsAsync();
        await WorkspaceTree.LoadAsync();
        RegisterBaseCommands();

        var settings = await _settingsService.GetSettingsAsync();
        if (settings.StartWithLastProject && !string.IsNullOrEmpty(settings.LastOpenedProjectId))
        {
            var lastProject = await _projectService.GetProjectAsync(settings.LastOpenedProjectId);
            if (lastProject != null)
            {
                OnProjectSelected(lastProject);
            }
        }

        ProcessMonitor.StartMonitoring();

        // Check for updates in the background
        _ = CheckForUpdateOnStartupAsync();
    }

    private async Task CheckForUpdateOnStartupAsync()
    {
        try
        {
            await Task.Delay(3000); // Small delay to not slow down startup
            var result = await _updateService.CheckForUpdateAsync();
            if (result.HasUpdate)
            {
                _notificationService.Notify(
                    $"Nova versão v{result.LatestVersion} disponível!",
                    NotificationType.Info,
                    NotificationSource.System,
                    message: "Acesse Configurações > Sobre para atualizar.");
            }
        }
        catch { }
    }

    /// <summary>
    /// Switches to a different workspace: disposes current terminals, loads target layout,
    /// and restores terminal sessions (similar to SwitchProjectAsync).
    /// </summary>
    private async Task SwitchWorkspaceAsync(string workspaceId)
    {
        // Clean up handler references for current terminals
        foreach (var t in Terminals.ToList())
        {
            if (_terminalPropertyHandlers.TryGetValue(t, out var handler))
            {
                t.PropertyChanged -= handler;
                _terminalPropertyHandlers.Remove(t);
            }
        }
        Terminals.Clear();
        ActiveTerminal = null;
        ActiveTerminalCount = 0;

        // Service handles: save current state, dispose canvas terminals, load target, fire ActiveWorkspaceChanged
        await _workspaceService.SwitchWorkspaceAsync(workspaceId);

        // Restore canvas items from the loaded workspace
        var workspace = _workspaceService.CurrentWorkspace;
        if (workspace?.Items is { Count: > 0 })
        {
            // Restore camera position
            if (workspace.Camera is not null)
                CanvasViewModel.SyncCamera(workspace.Camera.OffsetX, workspace.Camera.OffsetY, workspace.Camera.Zoom);

            // Restore terminal items with limited parallelism
            var terminalItems = workspace.Items.Where(i => i.Type == CanvasItemType.Terminal).ToList();
            var semaphore = new SemaphoreSlim(3, 3);
            var initTasks = new List<Task>();

            foreach (var itemModel in terminalItems)
            {
                var shellType = itemModel.Metadata.TryGetValue("shellType", out var st)
                    && Enum.TryParse<ShellType>(st, true, out var parsed)
                        ? parsed
                        : CurrentProject?.DefaultShell ?? ShellType.PowerShell;

                var workDir = itemModel.Metadata.TryGetValue("workingDirectory", out var wd)
                    ? wd
                    : CurrentProject?.Path;

                var terminalVm = _terminalVmFactory();
                Terminals.Add(terminalVm);

                var vm = terminalVm;
                initTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try { await vm.InitializeAsync(shellType, workDir, CurrentProject?.Id); }
                    finally { semaphore.Release(); }
                }));

                var canvasItem = _canvasItemFactory.CreateTerminalItemFromModel(terminalVm, itemModel);
                _workspaceService.AddRestoredItem(canvasItem);

                if (!WorkspaceTree.HasNodeForCanvasItem(canvasItem.Model.Id))
                    _ = WorkspaceTree.RegisterTerminalAsync(canvasItem.Title, canvasItem.Model.Id, CurrentProject?.Id);

                // Wire title sync handler
                var capturedItem = canvasItem;
                System.ComponentModel.PropertyChangedEventHandler titleHandler = async (_, args) =>
                {
                    if (args.PropertyName == nameof(TerminalCanvasItemViewModel.Title))
                        await WorkspaceTree.SyncTerminalTitleAsync(capturedItem.Model.Id, capturedItem.Title);
                };
                terminalVm.PropertyChanged += titleHandler;
                _terminalPropertyHandlers[terminalVm] = titleHandler;
            }

            // TODO: restore widget items (Git, Process, Note, etc.) if needed in the future

            await Task.WhenAll(initTasks);

            ActiveTerminal = Terminals.FirstOrDefault();
            ActiveTerminalCount = Terminals.Count;
        }
    }

    /// <summary>
    /// Creates a new terminal tab.
    /// </summary>
    [RelayCommand]
    private async Task NewTerminal()
    {
        var settings = await _settingsService.GetSettingsAsync();
        var shellType = CurrentProject?.DefaultShell ?? settings.DefaultShell;
        var workDir = CurrentProject?.Path;

        var terminalVm = _terminalVmFactory();
        Terminals.Add(terminalVm);
        ActiveTerminal = terminalVm;
        ActiveTerminalCount = Terminals.Count;

        await terminalVm.InitializeAsync(shellType, workDir, CurrentProject?.Id);

        // Register with workspace so it appears as a spatial canvas item
        _workspaceService.AddTerminalItem(terminalVm);

        // Auto-register the new canvas item in the workspace tree sidebar
        var canvasItems = _workspaceService.TerminalItems;
        var addedItem = canvasItems.LastOrDefault();
        if (addedItem is not null && !WorkspaceTree.HasNodeForCanvasItem(addedItem.Model.Id))
        {
            _ = WorkspaceTree.RegisterTerminalAsync(addedItem.Title, addedItem.Model.Id, CurrentProject?.Id);

            // Sync title changes to workspace tree when shell updates via OSC sequences
            var capturedItem = addedItem;
            System.ComponentModel.PropertyChangedEventHandler handler = async (_, args) =>
            {
                if (args.PropertyName == nameof(TerminalCanvasItemViewModel.Title))
                    await WorkspaceTree.SyncTerminalTitleAsync(capturedItem.Model.Id, capturedItem.Title);
            };
            terminalVm.PropertyChanged += handler;
            _terminalPropertyHandlers[terminalVm] = handler;
        }

        ShellTypeDisplay = shellType.GetDisplayName();
        CurrentView = ViewType.Terminal;
    }

    // ─── AI Quick-Launch Commands ────────────────────────────────────────

    [RelayCommand]
    private Task LaunchClaude() => _aiLauncher.LaunchAsync(AiSessionType.Claude);

    [RelayCommand]
    private Task LaunchClaudeResume() => _aiLauncher.LaunchAsync(AiSessionType.ClaudeResume);

    [RelayCommand]
    private Task LaunchCc() => _aiLauncher.LaunchAsync(AiSessionType.Cc);

    [RelayCommand]
    private Task LaunchCcSonnet() => _aiLauncher.LaunchAsync(AiSessionType.CcRun, "sonnet");

    [RelayCommand]
    private Task LaunchCcOpus() => _aiLauncher.LaunchAsync(AiSessionType.CcRun, "opus");

    [RelayCommand]
    private Task LaunchCcHaiku() => _aiLauncher.LaunchAsync(AiSessionType.CcRun, "haiku");

    [RelayCommand]
    private Task LaunchCcAgent() => _aiLauncher.LaunchAsync(AiSessionType.CcRun, "agent");

    [RelayCommand]
    private Task LaunchCcOpenRouter() => _aiLauncher.LaunchAsync(AiSessionType.CcOpenRouter);

    /// <summary>
    /// Closes a terminal tab.
    /// </summary>
    [RelayCommand]
    private async Task CloseTerminal(TerminalViewModel? terminal)
    {
        if (terminal == null) return;

        // Remove from workspace canvas first
        var canvasItem = _workspaceService.TerminalItems
            .FirstOrDefault(i => i.Terminal == terminal);
        if (canvasItem is not null)
        {
            _workspaceService.RemoveItem(canvasItem.Model.Id);
            _ = WorkspaceTree.UnregisterCanvasItemAsync(canvasItem.Model.Id);
        }

        // Unsubscribe PropertyChanged handler to prevent memory leak
        if (_terminalPropertyHandlers.TryGetValue(terminal, out var handler))
        {
            terminal.PropertyChanged -= handler;
            _terminalPropertyHandlers.Remove(terminal);
        }

        int index = Terminals.IndexOf(terminal);
        terminal.Dispose();
        Terminals.Remove(terminal);
        ActiveTerminalCount = Terminals.Count;

        // Select adjacent tab
        if (Terminals.Count > 0)
        {
            int newIndex = Math.Min(index, Terminals.Count - 1);
            ActiveTerminal = Terminals[newIndex];
        }
        else
        {
            ActiveTerminal = null;
            CurrentView = ViewType.Dashboard;
        }
    }

    /// <summary>
    /// Navigates to the next terminal tab.
    /// </summary>
    [RelayCommand]
    private void NextTerminal()
    {
        if (Terminals.Count <= 1 || ActiveTerminal == null) return;
        int index = Terminals.IndexOf(ActiveTerminal);
        int nextIndex = (index + 1) % Terminals.Count;
        ActiveTerminal = Terminals[nextIndex];
    }

    /// <summary>
    /// Navigates to the previous terminal tab.
    /// </summary>
    [RelayCommand]
    private void PreviousTerminal()
    {
        if (Terminals.Count <= 1 || ActiveTerminal == null) return;
        int index = Terminals.IndexOf(ActiveTerminal);
        int prevIndex = (index - 1 + Terminals.Count) % Terminals.Count;
        ActiveTerminal = Terminals[prevIndex];
    }

    /// <summary>
    /// Cycles to the next project in the project list.
    /// </summary>
    [RelayCommand]
    private async Task NextProject()
    {
        var projects = await _projectService.GetAllProjectsAsync();
        if (projects.Count <= 1) return;

        int index = CurrentProject is null
            ? 0
            : projects.FindIndex(p => p.Id == CurrentProject.Id);

        int nextIndex = (index + 1) % projects.Count;
        OnProjectSelected(projects[nextIndex]);
    }

    /// <summary>
    /// Toggles sidebar visibility.
    /// </summary>
    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    [RelayCommand]
    private void ToggleTerminalPanel()
    {
        IsTerminalPanelVisible = !IsTerminalPanelVisible;
    }

    /// <summary>
    /// Switches the current view.
    /// </summary>
    [RelayCommand]
    private void NavigateTo(ViewType viewType)
    {
        CurrentView = viewType;
    }

    /// <summary>
    /// Abre a tela de configurações.
    /// </summary>
    [RelayCommand]
    private async Task OpenSettings()
    {
        var settingsVm = _settingsVmFactory();
        await settingsVm.InitializeAsync();
        settingsVm.CloseRequested += _ =>
        {
            CurrentView = ViewType.Terminal;
        };
        SettingsViewModel = settingsVm;
        CurrentView = ViewType.Settings;
    }

    // ─── Private Methods ─────────────────────────────────────────────────────

    private async void OnProjectSelected(Project project)
    {
        try
        {
            await SwitchProjectAsync(project);
        }
        catch (Exception ex)
        {
            StatusBarText = $"Erro ao abrir projeto: {ex.Message}";
            _notificationService.Notify(
                "Erro ao abrir projeto",
                NotificationType.Error,
                NotificationSource.System,
                message: ex.Message);
            System.Diagnostics.Debug.WriteLine($"[OnProjectSelected] {ex}");
        }
    }

    /// <summary>
    /// Saves current workspace, kills terminals, loads the target project's layout.
    /// Parallelizes independent operations for faster switching.
    /// </summary>
    private async Task SwitchProjectAsync(Project project)
    {
        if (!await _projectSwitchLock.WaitAsync(0)) return; // skip if already switching
        try
        {
            IsProjectSwitching = true;
            ProjectSwitchMessage = $"Carregando {project.Name}…";
            StatusBarText = $"Switching to {project.Name}… saving layout";

            // 1. Save current layout
            if (Terminals.Count > 0 || CurrentProject is not null)
                await CanvasViewModel.SaveCurrentLayoutAsync();

            ProjectSwitchMessage = $"Fechando terminais…";
            StatusBarText = $"Switching to {project.Name}… closing terminals";

            // 2. Clean up property handlers and kill all terminals
            foreach (var t in Terminals.ToList())
            {
                if (_terminalPropertyHandlers.TryGetValue(t, out var h))
                {
                    t.PropertyChanged -= h;
                    _terminalPropertyHandlers.Remove(t);
                }
                t.Dispose();
            }
            Terminals.Clear();
            ActiveTerminal = null;
            ActiveTerminalCount = 0;

            // 3. Clear canvas
            _workspaceService.ClearAll();

            // 4. Update current project
            CurrentProject = project;
            CanvasViewModel.CurrentProjectId = project.Id;
            CurrentView = ViewType.Terminal;

            ProjectSwitchMessage = $"Carregando informações do projeto…";
            StatusBarText = $"Switching to {project.Name}… loading project info";

            // 5. Persist settings & fetch git info in parallel
            var settingsTask = Task.Run(async () =>
            {
                var settings = await _settingsService.GetSettingsAsync();
                settings.LastOpenedProjectId = project.Id;
                await _settingsService.SaveSettingsAsync(settings);
            });

            var dashboardTask = Dashboard.SetProjectAsync(project);
            var gitTask = _gitService.GetGitInfoAsync(project.Path);

            await Task.WhenAll(settingsTask, dashboardTask, gitTask);

            Dashboard.StopRefresh();

            var gitInfo = gitTask.Result;
            GitBranchDisplay = gitInfo?.BranchDisplay ?? "";
            StatusBarText = $"Project: {project.Name}";

            _notificationService.Notify(
                $"Projeto '{project.Name}' carregado",
                NotificationType.Success,
                NotificationSource.System,
                message: gitInfo != null ? $"Branch: {gitInfo.Branch}" : null);

            StatusBarText = $"Switching to {project.Name}… restoring layout";

            // 6. Load target project layout
            var saved = await CanvasViewModel.LoadLayoutAsync(project.Id);
            if (saved?.Items is { Count: > 0 })
            {
                // Restore camera
                if (saved.Camera is not null)
                    CanvasViewModel.SyncCamera(saved.Camera.OffsetX, saved.Camera.OffsetY, saved.Camera.Zoom);

                // Restore terminal items with limited parallelism (max 3 concurrent)
                var terminalItems = saved.Items.Where(i => i.Type == CanvasItemType.Terminal).ToList();
                var semaphore = new SemaphoreSlim(3, 3);
                var initTasks = new List<Task>();

                ProjectSwitchMessage = $"Restaurando {terminalItems.Count} terminais…";
                StatusBarText = $"Switching to {project.Name}… restoring {terminalItems.Count} terminals";

                foreach (var itemModel in terminalItems)
                {
                    var shellType = itemModel.Metadata.TryGetValue("shellType", out var st)
                        && Enum.TryParse<ShellType>(st, true, out var parsed)
                            ? parsed
                            : project.DefaultShell;

                    var workDir = itemModel.Metadata.TryGetValue("workingDirectory", out var wd)
                        ? wd
                        : project.Path;

                    var terminalVm = _terminalVmFactory();
                    Terminals.Add(terminalVm);

                    // Capture for closure
                    var vm = terminalVm;
                    var model = itemModel;
                    initTasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            await vm.InitializeAsync(shellType, workDir, project.Id);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));

                    var canvasItem = _canvasItemFactory.CreateTerminalItemFromModel(terminalVm, itemModel);
                    _workspaceService.AddRestoredItem(canvasItem);

                    if (!WorkspaceTree.HasNodeForCanvasItem(canvasItem.Model.Id))
                        _ = WorkspaceTree.RegisterTerminalAsync(canvasItem.Title, canvasItem.Model.Id, project.Id);
                }

                await Task.WhenAll(initTasks);

                ActiveTerminal = Terminals.FirstOrDefault();
                ActiveTerminalCount = Terminals.Count;
            }

            StatusBarText = $"Project: {project.Name} — ready";
        }
        finally
        {
            IsProjectSwitching = false;
            ProjectSwitchMessage = string.Empty;
            _projectSwitchLock.Release();
        }
    }

    private void OnAddProjectRequested()
    {
        var editVm = _projectEditVmFactory();
        editVm.InitializeForAdd();
        editVm.CloseRequested += saved =>
        {
            IsProjectEditVisible = false;
            ProjectEditViewModel = null;
        };
        ProjectEditViewModel = editVm;
        IsProjectEditVisible = true;
    }

    private void OnEditProjectRequested(Project project)
    {
        var editVm = _projectEditVmFactory();
        editVm.InitializeForEdit(project);
        editVm.CloseRequested += saved =>
        {
            IsProjectEditVisible = false;
            ProjectEditViewModel = null;
        };
        ProjectEditViewModel = editVm;
        IsProjectEditVisible = true;
    }

    private void RegisterBaseCommands()
    {
        _commandPaletteService.Register(new Models.CommandDefinitionModel
        {
            Id = "terminal.new",
            Title = "Novo Terminal",
            Subtitle = "Abre um terminal no canvas",
            ShortcutHint = "Ctrl+Shift+T",
            Category = Models.CommandCategory.Terminal,
            IconKey = "TerminalIcon",
            Priority = 100,
            Execute = () => NewTerminalCommand.Execute(null)
        });

        _commandPaletteService.Register(new Models.CommandDefinitionModel
        {
            Id = "canvas.fitall",
            Title = "Encaixar Todos no Canvas",
            Subtitle = "Zoom para mostrar todos os terminais",
            Category = Models.CommandCategory.Layout,
            IconKey = "ScanIcon",
            Priority = 80,
            Execute = () => CanvasViewModel.FitAllCommand.Execute(null)
        });

        _commandPaletteService.Register(new Models.CommandDefinitionModel
        {
            Id = "view.dashboard",
            Title = "Ir para Dashboard",
            Category = Models.CommandCategory.Navigation,
            IconKey = "DashboardIcon",
            Execute = () => CurrentView = ViewType.Dashboard
        });

        _commandPaletteService.Register(new Models.CommandDefinitionModel
        {
            Id = "view.canvas",
            Title = "Ir para Canvas",
            Category = Models.CommandCategory.Navigation,
            IconKey = "TerminalIcon",
            Execute = () => CurrentView = ViewType.Terminal
        });

        _commandPaletteService.Register(new Models.CommandDefinitionModel
        {
            Id = "app.settings",
            Title = "Configurações",
            ShortcutHint = "Ctrl+,",
            Category = Models.CommandCategory.Navigation,
            IconKey = "SettingsIcon",
            Execute = () => OpenSettingsCommand.Execute(null)
        });

        // Dynamic: one command per open terminal (incremental updates)
        _workspaceService.Items.CollectionChanged += OnWorkspaceItemsChanged;
    }

    private void OnWorkspaceItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    foreach (var item in e.NewItems.OfType<TerminalCanvasItemViewModel>())
                        RegisterTerminalCommand(item);
                }
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (var item in e.OldItems.OfType<TerminalCanvasItemViewModel>())
                        _commandPaletteService.Unregister($"terminal.focus.{item.Model.Id}");
                }
                break;
            default:
                RefreshTerminalCommands();
                break;
        }
    }

    private void RegisterTerminalCommand(TerminalCanvasItemViewModel tvm)
    {
        var captured = tvm;
        _commandPaletteService.Register(new Models.CommandDefinitionModel
        {
            Id = $"terminal.focus.{captured.Model.Id}",
            Title = $"Focar: {captured.Title}",
            Subtitle = captured.Terminal.Session?.WorkingDirectory,
            Category = Models.CommandCategory.Terminal,
            IconKey = "TerminalIcon",
            Priority = 50,
            Execute = () =>
            {
                CanvasViewModel.RequestFocus(captured);
                CurrentView = ViewType.Terminal;
            }
        });
    }

    private void RefreshTerminalCommands()
    {
        // Remove old dynamic terminal commands
        var oldIds = _commandPaletteService.GetAll()
            .Where(c => c.Id.StartsWith("terminal.focus."))
            .Select(c => c.Id)
            .ToList();
        foreach (var id in oldIds) _commandPaletteService.Unregister(id);

        // Re-register one per terminal
        foreach (var tvm in _workspaceService.TerminalItems)
            RegisterTerminalCommand(tvm);
    }

    private async Task RunCommandInNewTerminalAsync(string command)
    {
        await NewTerminal();
        if (ActiveTerminal != null)
        {
            // Small delay to let the shell start
            await Task.Delay(500);
            await ActiveTerminal.ExecuteCommandAsync(command);
        }
    }

    partial void OnIsAIPanelOpenChanged(bool value)
    {
        if (value)
            _ = AssistantPanel.OnPanelOpenedAsync();
        else
            AssistantPanel.OnPanelClosed();
    }

    partial void OnCurrentViewChanged(ViewType oldValue, ViewType newValue)
    {
        if (oldValue == ViewType.Dashboard && newValue != ViewType.Dashboard)
            Dashboard.StopRefresh();
        else if (newValue == ViewType.Dashboard && oldValue != ViewType.Dashboard)
            Dashboard.ResumeRefresh();
    }

    partial void OnActiveTerminalChanged(TerminalViewModel? value)
    {
        // Deactivate all, activate selected
        foreach (var t in Terminals)
            t.IsActive = false;

        if (value != null)
        {
            value.IsActive = true;
            ShellTypeDisplay = value.ShellType.GetDisplayName();
        }
    }
}

/// <summary>
/// Available view types for the main content area.
/// </summary>
public enum ViewType
{
    Dashboard,
    Terminal,
    ProcessMonitor,
    Settings,
    Browser
}
