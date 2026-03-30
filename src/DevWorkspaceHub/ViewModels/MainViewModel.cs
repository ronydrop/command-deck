using System.Collections.ObjectModel;
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
    private readonly SettingsService _settingsService;

    // ─── Sub ViewModels ──────────────────────────────────────────────────────

    public ProjectListViewModel ProjectList { get; }
    public DashboardViewModel Dashboard { get; }
    public ProcessMonitorViewModel ProcessMonitor { get; }

    // ─── Observable Properties ───────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<TerminalViewModel> _terminals = new();

    [ObservableProperty]
    private TerminalViewModel? _activeTerminal;

    [ObservableProperty]
    private Project? _currentProject;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [ObservableProperty]
    private ViewType _currentView = ViewType.Dashboard;

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

    [ObservableProperty]
    private bool _isProjectEditVisible;

    [ObservableProperty]
    private ProjectEditViewModel? _projectEditViewModel;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private SettingsViewModel? _settingsViewModel;

    public MainViewModel(
        ITerminalService terminalService,
        IProjectService projectService,
        IGitService gitService,
        IProcessMonitorService processMonitorService,
        SettingsService settingsService,
        ProjectListViewModel projectList,
        DashboardViewModel dashboard,
        ProcessMonitorViewModel processMonitor)
    {
        _terminalService = terminalService;
        _projectService = projectService;
        _gitService = gitService;
        _processMonitorService = processMonitorService;
        _settingsService = settingsService;

        ProjectList = projectList;
        Dashboard = dashboard;
        ProcessMonitor = processMonitor;

        // Wire up events
        ProjectList.ProjectSelected += OnProjectSelected;
        ProjectList.AddProjectRequested += OnAddProjectRequested;
        ProjectList.EditProjectRequested += OnEditProjectRequested;

        Dashboard.RunCommandRequested += async cmd => await RunCommandInNewTerminalAsync(cmd);
    }

    /// <summary>
    /// Initializes the application on startup.
    /// </summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        await ProjectList.LoadProjectsAsync();

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

        var terminalVm = new TerminalViewModel(_terminalService);
        Terminals.Add(terminalVm);
        ActiveTerminal = terminalVm;
        ActiveTerminalCount = Terminals.Count;

        await terminalVm.InitializeAsync(shellType, workDir, CurrentProject?.Id);

        ShellTypeDisplay = shellType.GetDisplayName();
        CurrentView = ViewType.Terminal;
    }

    /// <summary>
    /// Closes a terminal tab.
    /// </summary>
    [RelayCommand]
    private async Task CloseTerminal(TerminalViewModel? terminal)
    {
        if (terminal == null) return;

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
    /// Toggles sidebar visibility.
    /// </summary>
    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
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
    /// Abre o diálogo de configurações.
    /// </summary>
    [RelayCommand]
    private async Task OpenSettings()
    {
        var settingsVm = new SettingsViewModel(_settingsService);
        await settingsVm.InitializeAsync();
        settingsVm.CloseRequested += _ =>
        {
            IsSettingsVisible = false;
            SettingsViewModel = null;
        };
        SettingsViewModel = settingsVm;
        IsSettingsVisible = true;
    }

    // ─── Private Methods ─────────────────────────────────────────────────────

    private async void OnProjectSelected(Project project)
    {
        CurrentProject = project;
        CurrentView = ViewType.Dashboard;

        // Update settings with last opened project
        var settings = await _settingsService.GetSettingsAsync();
        settings.LastOpenedProjectId = project.Id;
        await _settingsService.SaveSettingsAsync(settings);

        // Update dashboard
        await Dashboard.SetProjectAsync(project);

        // Update status bar
        var gitInfo = await _gitService.GetGitInfoAsync(project.Path);
        GitBranchDisplay = gitInfo?.BranchDisplay ?? "";
        StatusBarText = $"Project: {project.Name}";
    }

    private void OnAddProjectRequested()
    {
        var editVm = new ProjectEditViewModel(_projectService);
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
        var editVm = new ProjectEditViewModel(_projectService);
        editVm.InitializeForEdit(project);
        editVm.CloseRequested += saved =>
        {
            IsProjectEditVisible = false;
            ProjectEditViewModel = null;
        };
        ProjectEditViewModel = editVm;
        IsProjectEditVisible = true;
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
    ProcessMonitor
}
