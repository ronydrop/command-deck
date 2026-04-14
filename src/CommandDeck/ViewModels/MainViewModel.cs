using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
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
    private readonly Func<SettingsViewModel> _settingsVmFactory;
    private readonly Func<ProjectEditViewModel> _projectEditVmFactory;
    private readonly CanvasItemFactory _canvasItemFactory;
    private readonly IAiTerminalLauncher _aiLauncher;
    private readonly IAgentSelectorService _agentSelectorService;
    private readonly IAiTerminalService _aiTerminalService;
    private readonly IProjectSwitchService _projectSwitchService;
    private readonly SemaphoreSlim _projectSwitchLock = new(1, 1);

    /// <summary>AI Floating Orb ViewModel.</summary>
    public AiOrbViewModel AiOrb { get; }

    // ─── Sub ViewModels ──────────────────────────────────────────────────────

    /// <summary>Manages terminal tab lifecycle (create, close, active tracking).</summary>
    public TerminalManagerViewModel TerminalManager { get; }

    public ProjectListViewModel ProjectList { get; }
    public DashboardViewModel Dashboard { get; }
    public ProcessMonitorViewModel ProcessMonitor { get; }
    public TerminalCanvasViewModel CanvasViewModel { get; }
    public CommandPaletteViewModel CommandPalette { get; }
    public WorkspaceTreeViewModel WorkspaceTree { get; }

    /// <summary>Tabbed terminal layout ViewModel.</summary>
    public TabbedTerminalViewModel TabbedTerminal { get; }

    /// <summary>Branch selector overlay ViewModel.</summary>
    public BranchSelectorViewModel BranchSelector { get; }

    [ObservableProperty] private bool _isCommandPaletteOpen;
    [ObservableProperty] private bool _isBranchSelectorOpen;

    // ─── Observable Properties ───────────────────────────────────────────────

    // Delegating wrappers — keep XAML bindings working without changes
    public ObservableCollection<TerminalViewModel> Terminals => TerminalManager.Terminals;
    public TerminalViewModel? ActiveTerminal
    {
        get => TerminalManager.ActiveTerminal;
        set => TerminalManager.ActiveTerminal = value;
    }
    public int ActiveTerminalCount => TerminalManager.ActiveTerminalCount;
    public string ShellTypeDisplay => TerminalManager.ShellTypeDisplay;

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
    private int _activeProcessCount;

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

    /// <summary>Default AI tool ID (persisted, drives dropdown checkmark).</summary>
    [ObservableProperty]
    private string _defaultAiToolId = "claude";

    /// <summary>AI tools detected on the system — drives the dynamic AI dropdown.</summary>
    [ObservableProperty]
    private ObservableCollection<AiToolInfo> _availableAiTools = new(new[]
    {
        new AiToolInfo { Id = "claude",        DisplayName = "claude",          SessionType = AiSessionType.Claude       },
        new AiToolInfo { Id = "claude-resume", DisplayName = "claude --resume", SessionType = AiSessionType.ClaudeResume },
    });

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
        TerminalManagerViewModel terminalManager,
        Func<SettingsViewModel> settingsVmFactory,
        Func<ProjectEditViewModel> projectEditVmFactory,
        ProjectListViewModel projectList,
        DashboardViewModel dashboard,
        ProcessMonitorViewModel processMonitor,
        CanvasItemFactory canvasItemFactory,
        IAiTerminalLauncher aiLauncher,
        IAgentSelectorService agentSelectorService,
        IAiTerminalService aiTerminalService,
        BranchSelectorViewModel branchSelector,
        AiOrbViewModel aiOrb,
        IProjectSwitchService projectSwitchService,
        TabbedTerminalViewModel tabbedTerminal)
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
        _settingsVmFactory = settingsVmFactory;
        _projectEditVmFactory = projectEditVmFactory;
        _canvasItemFactory = canvasItemFactory;
        _aiLauncher = aiLauncher;
        _agentSelectorService = agentSelectorService;
        _aiTerminalService = aiTerminalService;
        _projectSwitchService = projectSwitchService;

        // Update ViewModel observable properties from the service result
        _projectSwitchService.SwitchCompleted += OnProjectSwitchCompleted;

        TerminalManager = terminalManager;

        // Propagate TerminalManager property changes to XAML bindings on MainViewModel
        TerminalManager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TerminalManager.ActiveTerminal)
                               or nameof(TerminalManager.ActiveTerminalCount)
                               or nameof(TerminalManager.ShellTypeDisplay))
                OnPropertyChanged(e.PropertyName);
        };

        ProjectList = projectList;
        Dashboard = dashboard;
        ProcessMonitor = processMonitor;
        CanvasViewModel = canvasViewModel;
        CommandPalette = commandPalette;
        WorkspaceTree = workspaceTree;
        BranchSelector = branchSelector;
        AiOrb = aiOrb;
        TabbedTerminal = tabbedTerminal;

        // Wire TerminalManager context and events
        TerminalManager.CurrentProject = null; // will be set on project switch
        TerminalManager.CanvasViewModel = CanvasViewModel;
        TerminalManager.AllTerminalsClosed += (_, _) => CurrentView = ViewType.Dashboard;

        CanvasViewModel.AddTerminalRequested += async () =>
        {
            TerminalManager.CurrentProject = CurrentProject;
            await TerminalManager.NewTerminal();
            CurrentView = ViewType.Terminal;
        };

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
    /// Toggles the sidebar panel (Ctrl+\).
    /// </summary>
    [RelayCommand]
    private void ToggleAllPanels()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
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

        // Clean orphan terminal nodes from the workspace tree (nodes whose
        // linked canvas item no longer exists in any saved workspace).
        var validIds = new HashSet<string>(
            _workspaceService.CurrentWorkspace?.Items.Select(i => i.Id) ?? []);
        await WorkspaceTree.ValidateAgainstCanvasAsync(validIds);

        await AiOrb.InitializeAsync();
        _ = LoadAvailableAiToolsAsync();
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Update check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a new terminal tab (delegates to <see cref="TerminalManagerViewModel"/>).
    /// </summary>
    [RelayCommand]
    private async Task NewTerminal()
    {
        TerminalManager.CurrentProject = CurrentProject;
        await TerminalManager.NewTerminal();
        CurrentView = ViewType.Terminal;
    }

    // ─── AI Quick-Launch Commands ────────────────────────────────────────

    [RelayCommand]
    private Task LaunchClaude() => _aiLauncher.LaunchAsync(AiSessionType.Claude);

    [RelayCommand]
    private Task LaunchClaudeResume() => _aiLauncher.LaunchAsync(AiSessionType.ClaudeResume);

    /// <summary>
    /// Selects the given tool as default and immediately launches a terminal with it.
    /// Called from the AI dropdown menu items.
    /// </summary>
    [RelayCommand]
    private async Task LaunchAiTool(string toolId)
    {
        var agent = _agentSelectorService.Agents.FirstOrDefault(a => a.Id == toolId);
        if (agent is null) return;

        DefaultAiToolId = toolId;
        _agentSelectorService.SelectAgent(toolId);
        await _aiLauncher.LaunchAsync(agent.SessionType);
    }

    private async Task LoadAvailableAiToolsAsync()
    {
        try
        {
            // All possible tools with their session types and launch commands
            var allTools = new List<(string Id, string DisplayName, AiSessionType SessionType, string CliCheck)>
            {
                ("claude",        "claude",          AiSessionType.Claude,       "claude"),
                ("claude-resume", "claude --resume", AiSessionType.ClaudeResume, "claude"),
                ("codex",         "codex",           AiSessionType.Codex,        "codex"),
                ("aider",         "aider",           AiSessionType.Aider,        "aider"),
                ("gemini",        "gemini",          AiSessionType.Gemini,       "gemini"),
                ("copilot",       "copilot",         AiSessionType.Copilot,      "gh"),
            };

            var settings = await _settingsService.GetSettingsAsync();
            DefaultAiToolId = settings.DefaultAiToolId;

            var detectedTools = await Task.Run(() =>
            {
                var result = new List<AiToolInfo>();
                foreach (var (id, displayName, sessionType, cliCheck) in allTools)
                {
                    if (AiTerminalService.CheckCommandExists(cliCheck))
                    {
                        result.Add(new AiToolInfo
                        {
                            Id = id,
                            DisplayName = displayName,
                            SessionType = sessionType
                        });
                    }
                }
                return result;
            });

            // Fallback: if detection fails entirely, keep default items so the dropdown isn't empty
            if (detectedTools.Count == 0)
            {
                detectedTools.Add(new AiToolInfo { Id = "claude",        DisplayName = "claude",          SessionType = AiSessionType.Claude       });
                detectedTools.Add(new AiToolInfo { Id = "claude-resume", DisplayName = "claude --resume", SessionType = AiSessionType.ClaudeResume });
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                AvailableAiTools = new ObservableCollection<AiToolInfo>(detectedTools);
                UpdateDefaultToolMarker();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] LoadAvailableAiToolsAsync failed: {ex.Message}");
            // AvailableAiTools retains its default items — dropdown still works
        }
    }

    private void UpdateDefaultToolMarker()
    {
        foreach (var tool in AvailableAiTools)
            tool.IsDefault = tool.Id == DefaultAiToolId;
    }

    partial void OnDefaultAiToolIdChanged(string value) => UpdateDefaultToolMarker();

    /// <summary>
    /// Closes a terminal tab (delegates to <see cref="TerminalManagerViewModel"/>).
    /// </summary>
    [RelayCommand]
    private Task CloseTerminal(TerminalViewModel? terminal)
        => TerminalManager.CloseTerminal(terminal);

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

        // Show overlay BEFORE any synchronous UI-thread work so the spinner is
        // visible during the teardown below (Terminals.Clear fires CollectionChanged).
        IsProjectSwitching = true;
        ProjectSwitchMessage = $"Carregando {project.Name}…";
        StatusBarText = $"Switching to {project.Name}…";

        // Force the dispatcher to process the Render pass so the overlay is actually
        // painted on screen before the heavy synchronous work below blocks the UI thread.
        await Application.Current.Dispatcher.InvokeAsync(
            static () => { }, System.Windows.Threading.DispatcherPriority.Render);

        // Snapshot of ViewModel-owned state consumed by the service
        var activeTerminals = TerminalManager.Terminals.ToList();

        // Update CurrentProject and view before handing off to the service so that
        // bindings that read CurrentProject are consistent during the switch.
        CurrentProject = project;
        TerminalManager.CurrentProject = project;
        CurrentView = ViewType.Terminal;
        TerminalManager.Terminals.Clear();
        TerminalManager.ActiveTerminal = null;
        TerminalManager.ActiveTerminalCount = 0;

        var context = new ProjectSwitchContext
        {
            CurrentProjectId = CanvasViewModel.CurrentProjectId,
            ActiveTerminals = activeTerminals,
            TerminalVmFactory = TerminalManager.TerminalVmFactory,
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
            // Defer hiding the overlay until WPF has finished rendering the canvas cards.
            // Using DispatcherPriority.Loaded ensures all layout/render passes complete first.
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsProjectSwitching = false;
                ProjectSwitchMessage = string.Empty;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
            _projectSwitchLock.Release();
        }
    }

    /// <summary>
    /// Applies the service result to ViewModel observable properties.
    /// Scalar properties are safe to set from any thread via CommunityToolkit
    /// property setters. <see cref="ObservableCollection{T}"/> mutations must
    /// run on the UI thread to avoid cross-thread collection-change exceptions.
    /// </summary>
    private void OnProjectSwitchCompleted(object? sender, ProjectSwitchResult result)
    {
        if (!result.Success) return;

        // Scalar properties — safe via CommunityToolkit property setters
        CurrentProject = result.SwitchedTo;
        GitBranchDisplay = result.GitBranchDisplay ?? string.Empty;

        // ObservableCollection — must be on UI thread
        Application.Current?.Dispatcher.Invoke(() =>
        {
            TerminalManager.ApplySwitchResult(result);
        });

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
            Execute = () => NewTerminalCommand.Execute(null)  // delegates to TerminalManager internally
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
        TerminalManager.CurrentProject = CurrentProject;
        await TerminalManager.RunCommandInNewTerminalAsync(command);
        CurrentView = ViewType.Terminal;
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
    TabbedTerminal
}
