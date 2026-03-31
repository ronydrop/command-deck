using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel for the project dashboard showing git status, processes, and terminal overview.
/// </summary>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly IGitService _gitService;
    private readonly IProcessMonitorService _processMonitorService;
    private readonly ITerminalService _terminalService;
    private readonly INotificationService _notificationService;
    private readonly DispatcherTimer _refreshTimer;
    private string? _lastKnownBranch;

    [ObservableProperty]
    private Project? _currentProject;

    [ObservableProperty]
    private GitInfo? _gitInfo;

    [ObservableProperty]
    private bool _hasGitInfo;

    [ObservableProperty]
    private int _activeTerminalCount;

    [ObservableProperty]
    private int _runningProcessCount;

    [ObservableProperty]
    private string _welcomeMessage = $"Welcome, {Environment.UserName}";

    [ObservableProperty]
    private string _currentTime = DateTime.Now.ToString("HH:mm:ss");

    [ObservableProperty]
    private string _currentDate = DateTime.Now.ToString("dddd, MMMM dd, yyyy");

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    /// <summary>
    /// Quick-access startup commands for the current project.
    /// </summary>
    [ObservableProperty]
    private List<string> _startupCommands = new();

    [ObservableProperty]
    private List<GitCommitInfo> _recentCommits = new();

    [ObservableProperty]
    private List<GitFileChange> _changedFiles = new();

    [ObservableProperty]
    private bool _hasChangedFiles;

    /// <summary>
    /// Event to request opening a terminal with a specific command.
    /// </summary>
    public event Action<string>? RunCommandRequested;

    public DashboardViewModel(
        IGitService gitService,
        IProcessMonitorService processMonitorService,
        ITerminalService terminalService,
        INotificationService notificationService)
    {
        _gitService = gitService;
        _processMonitorService = processMonitorService;
        _terminalService = terminalService;
        _notificationService = notificationService;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += async (_, _) =>
        {
            try { await RefreshAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[DashboardTick] {ex}"); }
        };
    }

    /// <summary>
    /// Sets the current project and starts refreshing data.
    /// </summary>
    public async Task SetProjectAsync(Project project)
    {
        CurrentProject = project;
        StartupCommands = project.StartupCommands.ToList();
        _refreshTimer.Start();
        await RefreshAsync();
    }

    /// <summary>
    /// Refreshes all dashboard data.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
            CurrentDate = DateTime.Now.ToString("dddd, MMMM dd, yyyy");

            // Update terminal count
            ActiveTerminalCount = _terminalService.GetSessions().Count(s => s.Status == TerminalStatus.Running);

            // Update git info
            if (CurrentProject != null)
            {
                GitInfo = await _gitService.GetGitInfoAsync(CurrentProject.Path);
                HasGitInfo = GitInfo != null;
                if (GitInfo != null)
                {
                    CurrentProject.GitInfo = GitInfo;
                    CheckGitNotifications(GitInfo);
                }

                RecentCommits = await _gitService.GetRecentCommitsAsync(CurrentProject.Path);
                ChangedFiles = await _gitService.GetChangedFilesAsync(CurrentProject.Path);
                HasChangedFiles = ChangedFiles.Count > 0;
            }

            // Update process count
            var processes = await _processMonitorService.GetRunningProcessesAsync();
            RunningProcessCount = processes.Count;

            StatusMessage = $"Last updated: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Executes a startup command in a new terminal.
    /// </summary>
    [RelayCommand]
    private void RunStartupCommand(string? command)
    {
        if (string.IsNullOrEmpty(command)) return;
        RunCommandRequested?.Invoke(command);
    }

    /// <summary>
    /// Executes a Git action (pull, push, fetch, stash) in a new terminal.
    /// </summary>
    [RelayCommand]
    private void RunGitAction(string? action)
    {
        if (string.IsNullOrEmpty(action) || CurrentProject == null) return;
        var command = action switch
        {
            "pull" => "git pull",
            "push" => "git push",
            "fetch" => "git fetch",
            "stash" => "git stash",
            _ => null
        };
        if (command != null)
        {
            _notificationService.Notify(
                $"Executando {command}...",
                NotificationType.Progress,
                NotificationSource.Git);
            RunCommandRequested?.Invoke(command);
        }
    }

    /// <summary>
    /// Checks git state changes and emits notifications for important events.
    /// </summary>
    private void CheckGitNotifications(GitInfo info)
    {
        // Branch change detection
        if (_lastKnownBranch != null && _lastKnownBranch != info.Branch)
        {
            _notificationService.Notify(
                $"Branch alterada: {_lastKnownBranch} → {info.Branch}",
                NotificationType.Info,
                NotificationSource.Git);
        }
        _lastKnownBranch = info.Branch;

        // Conflict detection
        if (info.ConflictedFiles > 0)
        {
            _notificationService.Notify(
                $"{info.ConflictedFiles} arquivo(s) em conflito",
                NotificationType.Warning,
                NotificationSource.Git,
                message: "Resolva os conflitos antes de continuar.");
        }
    }

    /// <summary>
    /// Stops auto-refresh when this view is not visible.
    /// </summary>
    public void StopRefresh()
    {
        _refreshTimer.Stop();
    }

    /// <summary>
    /// Resumes auto-refresh when navigating back to the dashboard.
    /// </summary>
    public void ResumeRefresh()
    {
        if (CurrentProject != null)
            _refreshTimer.Start();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
