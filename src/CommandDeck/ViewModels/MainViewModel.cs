using System.Collections.ObjectModel;
using System.Diagnostics;
using Debug = System.Diagnostics.Trace;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

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
    private readonly IProjectSwitchService _projectSwitchService;
    private readonly SemaphoreSlim _projectSwitchLock = new(1, 1);

    /// <summary>AI Floating Orb ViewModel.</summary>
    public AiOrbViewModel AiOrb { get; }

    /// <summary>Dynamic Island overlay ViewModel.</summary>
    public DynamicIslandViewModel DynamicIsland { get; }
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

    /// <summary>Branch selector overlay ViewModel.</summary>
    public BranchSelectorViewModel BranchSelector { get; }

    [ObservableProperty] private bool _isCommandPaletteOpen;
    [ObservableProperty] private bool _isBranchSelectorOpen;

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

    /// <summary>Controls visibility of the keyboard shortcuts cheat-sheet overlay.</summary>
    [ObservableProperty]
    private bool _isShortcutsOverlayOpen;

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
        IAiTerminalLauncher aiLauncher,
        BranchSelectorViewModel branchSelector,
        AiOrbViewModel aiOrb,
        DynamicIslandViewModel dynamicIsland,
        IProjectSwitchService projectSwitchService)
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
        _projectSwitchService = projectSwitchService;

        // Update ViewModel observable properties from the service result
        _projectSwitchService.SwitchCompleted += OnProjectSwitchCompleted;

        ProjectList = projectList;
        Dashboard = dashboard;
        ProcessMonitor = processMonitor;
        CanvasViewModel = canvasViewModel;
        CommandPalette = commandPalette;
        WorkspaceTree = workspaceTree;
        AssistantPanel = assistantPanel;
        Browser = browserViewModel;
        BranchSelector = branchSelector;
        AiOrb = aiOrb;
        DynamicIsland = dynamicIsland;

        CommandPalette.CloseRequested += () => IsCommandPaletteOpen = false;

        BranchSelector.CloseRequested += () => IsBranchSelectorOpen = false;
        BranchSelector.BranchSwitched += async branch =>
        {
            GitBranchDisplay = branch;
            try { await Dashboard.RefreshAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BranchSwitch] {ex}"); }
        };

        // Wire up events
        ProjectList.ProjectSelected += async p => await OnProjectSelectedAsync(p);
        ProjectList.AddProjectRequested += OnAddProjectRequested;
        ProjectList.EditProjectRequested += OnEditProjectRequested;

        Dashboard.RunCommandRequested += async cmd =>
        {
            try { await RunCommandInNewTerminalAsync(cmd); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RunCommand] {ex}"); }
        };

        Dashboard.OpenBranchSelectorRequested += () => OpenBranchSelectorCommand.Execute(null);

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
    /// Toggles the Dynamic Island overlay visibility (Ctrl+Shift+D).
    /// </summary>
    [RelayCommand]
    private void ToggleDynamicIsland()
        => DynamicIsland.ToggleVisibilityCommand.Execute(null);

    /// <summary>
    /// Navigates to the terminal session with the given session ID.
    /// Called by DynamicIslandViewModel when the user clicks a session row.
    /// </summary>
    public void FocusSessionById(string sessionId)
    {
        var terminal = Terminals.FirstOrDefault(t => t.Session?.Id == sessionId);
        if (terminal != null)
        {
            ActiveTerminal = terminal;
            CurrentView = ViewType.Terminal;
        }
    }

    /// <summary>
    /// Opens the branch selector overlay for the current project (Ctrl+Shift+G).
    /// </summary>
    [RelayCommand]
    private async Task OpenBranchSelector()
    {
        if (CurrentProject == null) return;
        IsBranchSelectorOpen = true;
        await BranchSelector.OpenAsync(CurrentProject.Path);
    }

    /// <summary>
    /// Toggles the AI Assistant side-panel open/closed (Ctrl+I).
    /// </summary>
    [RelayCommand]
    private void ToggleAIPanel() => IsAIPanelOpen = !IsAIPanelOpen;

    /// <summary>
    /// Toggles both side panels (sidebar + AI chat) simultaneously (Ctrl+\).
    /// Expands both if either is hidden; collapses both if both are open.
    /// </summary>
    [RelayCommand]
    private void ToggleAllPanels()
    {
        bool expand = IsSidebarCollapsed || !IsAIPanelOpen;
        IsSidebarCollapsed = !expand;
        IsAIPanelOpen = expand;
    }

    /// <summary>Opens the keyboard shortcuts cheat-sheet overlay (Ctrl+/).</summary>
    [RelayCommand]
    private void OpenShortcutsOverlay() => IsShortcutsOverlayOpen = true;

    /// <summary>Closes the keyboard shortcuts cheat-sheet overlay.</summary>
    [RelayCommand]
    private void CloseShortcutsOverlay() => IsShortcutsOverlayOpen = false;

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
        await AiOrb.InitializeAsync();
        RegisterBaseCommands();

        var settings = await _settingsService.GetSettingsAsync();
        if (settings.StartWithLastProject && !string.IsNullOrEmpty(settings.LastOpenedProjectId))
        {
            var lastProject = await _projectService.GetProjectAsync(settings.LastOpenedProjectId);
            if (lastProject != null)
            {
                await OnProjectSelectedAsync(lastProject);
            }
        }

        // ProcessMonitor starts lazily when Dashboard or Process panel becomes visible

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

        await terminalVm.PrepareAsync(shellType, workDir, CurrentProject?.Id);

        // Register with workspace so it appears as a spatial canvas item
        _workspaceService.AddTerminalItem(terminalVm);

        // Auto-register the new canvas item in the workspace tree sidebar
        var canvasItems = _workspaceService.TerminalItems;
        var addedItem = canvasItems.LastOrDefault();
        if (addedItem is not null && !WorkspaceTree.HasNodeForCanvasItem(addedItem.Model.Id))
        {
            _ = WorkspaceTree.RegisterTerminalAsync(addedItem.Title, addedItem.Model.Id, CurrentProject?.Id)
                .ContinueWith(t => System.Diagnostics.Debug.WriteLine($"[WorkspaceTree] RegisterTerminalAsync error: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);

            // Sync title changes to workspace tree when shell updates via OSC sequences
            var capturedItem = addedItem;
            System.ComponentModel.PropertyChangedEventHandler handler = async (_, args) =>
            {
                if (args.PropertyName == nameof(TerminalCanvasItemViewModel.Title))
                    try { await WorkspaceTree.SyncTerminalTitleAsync(capturedItem.Model.Id, capturedItem.Title); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[WorkspaceTree] SyncTerminalTitleAsync error: {ex}"); }
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
            _ = WorkspaceTree.UnregisterCanvasItemAsync(canvasItem.Model.Id)
                .ContinueWith(t => System.Diagnostics.Debug.WriteLine($"[WorkspaceTree] UnregisterCanvasItemAsync error: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
        }

        // Unsubscribe PropertyChanged handler to prevent memory leak
        if (_terminalPropertyHandlers.TryGetValue(terminal, out var handler))
        {
            terminal.PropertyChanged -= handler;
            _terminalPropertyHandlers.Remove(terminal);
        }

        int index = Terminals.IndexOf(terminal);
        await terminal.DisposeAsync();
        Terminals.Remove(terminal);
        ActiveTerminalCount = Terminals.Count;

        // Select adjacent tab
        if (Terminals.Count > 0)
        {
            int newIndex = index >= 0 ? Math.Min(index, Terminals.Count - 1) : 0;
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
        await OnProjectSelectedAsync(projects[nextIndex]);
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

    private async Task OnProjectSelectedAsync(Project project)
    {
        Debug.WriteLine($"[Perf] OnProjectSelected: {project.Name} (current: {CurrentProject?.Name})");

        if (CurrentView == ViewType.Settings)
            CurrentView = ViewType.Terminal;

        if (project.Id == CurrentProject?.Id)
        {
            Debug.WriteLine("[Perf] OnProjectSelected: SKIPPED (same project)");
            return;
        }

        if (!await _projectSwitchLock.WaitAsync(0)) return; // skip if already switching

        // Snapshot of ViewModel-owned state consumed by the service
        var activeTerminals = Terminals.ToList();

        // Clean up property handlers on the UI thread before disposal
        foreach (var t in activeTerminals)
        {
            if (_terminalPropertyHandlers.TryGetValue(t, out var h))
            {
                t.PropertyChanged -= h;
                _terminalPropertyHandlers.Remove(t);
            }
        }

        // Update CurrentProject and view before handing off to the service so that
        // bindings that read CurrentProject are consistent during the switch.
        CurrentProject = project;
        CurrentView = ViewType.Terminal;
        Terminals.Clear();
        ActiveTerminal = null;
        ActiveTerminalCount = 0;

        IsProjectSwitching = true;
        ProjectSwitchMessage = $"Carregando {project.Name}…";
        StatusBarText = $"Switching to {project.Name}…";

        var context = new ProjectSwitchContext
        {
            CurrentProjectId = CanvasViewModel.CurrentProjectId,
            ActiveTerminals = activeTerminals,
            ActiveTerminal = null,
            TerminalVmFactory = _terminalVmFactory,
        };

        var progress = new Progress<string>(msg =>
        {
            StatusBarText = msg;
            ProjectSwitchMessage = msg;
        });

        try
        {
            await _projectSwitchService.SwitchToAsync(project, context, progress);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Perf] OnProjectSelected ERROR: {ex}");
            StatusBarText = $"Erro ao abrir projeto: {ex.Message}";
            _notificationService.Notify(
                "Erro ao abrir projeto",
                NotificationType.Error,
                NotificationSource.System,
                message: ex.Message);
        }
        finally
        {
            IsProjectSwitching = false;
            ProjectSwitchMessage = string.Empty;
            _projectSwitchLock.Release();
        }
    }

    /// <summary>
    /// Applies the service result to ViewModel observable properties.
    /// Runs on the thread-pool; WPF dispatcher marshalling is handled by
    /// <see cref="ObservableObject"/> property setters and the UI bindings.
    /// </summary>
    private void OnProjectSwitchCompleted(object? sender, ProjectSwitchResult result)
    {
        if (!result.Success) return;

        // Populate the Terminals collection from the restored VMs
        foreach (var vm in result.RestoredTerminals)
            Terminals.Add(vm);

        ActiveTerminal = result.ActiveTerminal ?? Terminals.FirstOrDefault();
        ActiveTerminalCount = Terminals.Count;
        GitBranchDisplay = result.GitBranchDisplay ?? string.Empty;
        StatusBarText = $"Project: {result.SwitchedTo.Name} — ready";
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

        // Lazy start/stop process monitor based on visibility
        var needsMonitor = newValue is ViewType.Dashboard or ViewType.ProcessMonitor;
        var hadMonitor = oldValue is ViewType.Dashboard or ViewType.ProcessMonitor;
        if (needsMonitor && !hadMonitor)
            ProcessMonitor.StartMonitoring();
        else if (!needsMonitor && hadMonitor)
            ProcessMonitor.StopMonitoring();
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
