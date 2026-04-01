using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the project dashboard showing git status, processes, and terminal overview.
/// </summary>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly IGitService _gitService;
    private readonly IProcessMonitorService _processMonitorService;
    private readonly ITerminalService _terminalService;
    private readonly INotificationService _notificationService;
    private readonly IExternalEditorService _externalEditorService;
    private readonly IAssistantService _assistantService;
    private readonly IGitAiService _gitAiService;
    private readonly IClaudeUsageService _claudeUsage;
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

    [ObservableProperty]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private bool _isCommitInputVisible;

    // ─── Claude usage stats ────────────────────────────────────────────────
    [ObservableProperty]
    private string _claudeTokensDisplay = "0";

    [ObservableProperty]
    private string _claudeCostUsdDisplay = "$0.0000";

    [ObservableProperty]
    private string _claudeCostBrlDisplay = "R$ 0,0000";

    [ObservableProperty]
    private string _claudeModelDisplay = "—";

    /// <summary>
    /// Event to request opening a terminal with a specific command.
    /// </summary>
    public event Action<string>? RunCommandRequested;

    /// <summary>
    /// Event to request opening the branch selector overlay.
    /// </summary>
    public event Action? OpenBranchSelectorRequested;

    /// <summary>
    /// Worktree selector popup ViewModel.
    /// </summary>
    public WorktreeSelectorViewModel WorktreeSelector { get; }

    public DashboardViewModel(
        IGitService gitService,
        IProcessMonitorService processMonitorService,
        ITerminalService terminalService,
        INotificationService notificationService,
        IExternalEditorService externalEditorService,
        IAssistantService assistantService,
        IGitAiService gitAiService,
        WorktreeSelectorViewModel worktreeSelector,
        IClaudeUsageService claudeUsage)
    {
        _gitService = gitService;
        _assistantService = assistantService;
        _gitAiService = gitAiService;
        _processMonitorService = processMonitorService;
        _terminalService = terminalService;
        _notificationService = notificationService;
        _externalEditorService = externalEditorService;
        _claudeUsage = claudeUsage;
        WorktreeSelector = worktreeSelector;

        _claudeUsage.UsageUpdated += RefreshClaudeStats;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _refreshTimer.Tick += async (_, _) =>
        {
            try { await RefreshAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[DashboardTick] {ex}"); }
        };
    }

    private void RefreshClaudeStats()
    {
        var total = _claudeUsage.SessionTotalTokens;
        ClaudeTokensDisplay = total >= 1_000_000
            ? $"{total / 1_000_000.0:F1}M"
            : total >= 1_000
                ? $"{total / 1_000.0:F1}k"
                : total.ToString();

        ClaudeCostUsdDisplay = $"${_claudeUsage.SessionCostUsd:F4}";
        ClaudeCostBrlDisplay = $"R$ {_claudeUsage.SessionCostBrl:F4}";
        ClaudeModelDisplay   = _claudeUsage.CurrentModel ?? "—";
    }

    /// <summary>
    /// Sets the current project and starts refreshing data.
    /// Delays the initial refresh by 1.5s to let the UI render first before heavy git/WMI operations.
    /// </summary>
    public async Task SetProjectAsync(Project project)
    {
        CurrentProject = project;
        StartupCommands = project.StartupCommands.ToList();
        _refreshTimer.Stop();
        await RefreshAsync();
    }

    /// <summary>
    /// Refreshes all dashboard data.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
            CurrentDate = DateTime.Now.ToString("dddd, MMMM dd, yyyy");

            // Update terminal count (synchronous, no I/O)
            ActiveTerminalCount = _terminalService.GetSessions().Count(s => s.Status == TerminalStatus.Running);

            // Run all I/O-bound operations in parallel to minimize total latency
            var processesTask = _processMonitorService.GetRunningProcessesAsync();

            if (CurrentProject != null)
            {
                var gitInfoTask = _gitService.GetGitInfoAsync(CurrentProject.Path);
                var recentCommitsTask = _gitService.GetRecentCommitsAsync(CurrentProject.Path);
                var changedFilesTask = _gitService.GetChangedFilesAsync(CurrentProject.Path);

                await Task.WhenAll(gitInfoTask, recentCommitsTask, changedFilesTask, processesTask);

                GitInfo = gitInfoTask.Result;
                HasGitInfo = GitInfo != null;
                if (GitInfo != null)
                {
                    CurrentProject.GitInfo = GitInfo;
                    CheckGitNotifications(GitInfo);
                }

                RecentCommits = recentCommitsTask.Result;
                ChangedFiles = changedFilesTask.Result;
                HasChangedFiles = ChangedFiles.Count > 0;
            }
            else
            {
                await processesTask;
            }

            RunningProcessCount = processesTask.Result.Count;

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

    [RelayCommand]
    private void GitCommit()
    {
        if (CurrentProject == null) return;
        CommitMessage = string.Empty;
        IsCommitInputVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmCommit()
    {
        if (CurrentProject == null || string.IsNullOrWhiteSpace(CommitMessage)) return;

        IsCommitInputVisible = false;
        _notificationService.Notify("Criando commit...", NotificationType.Progress, NotificationSource.Git);

        var stageResult = await _gitService.StageAllAsync(CurrentProject.Path);
        if (!stageResult.Success)
        {
            _notificationService.Notify(stageResult.ErrorMessage ?? "Falha ao fazer stage", NotificationType.Error, NotificationSource.Git);
            return;
        }

        var commitResult = await _gitService.CommitAsync(CurrentProject.Path, CommitMessage);
        if (commitResult.Success)
        {
            _notificationService.Notify("Commit criado com sucesso!", NotificationType.Success, NotificationSource.Git);
            CommitMessage = string.Empty;
            await RefreshAsync();
        }
        else
        {
            _notificationService.Notify(commitResult.ErrorMessage ?? "Falha ao criar commit", NotificationType.Error, NotificationSource.Git);
        }
    }

    [RelayCommand]
    private void CancelCommit()
    {
        IsCommitInputVisible = false;
        CommitMessage = string.Empty;
    }

    [RelayCommand]
    private async Task AiCommitAndPush()
    {
        if (CurrentProject == null) return;

        if (!_assistantService.IsAnyProviderAvailable)
        {
            _notificationService.Notify("Nenhum provedor de IA configurado.", NotificationType.Warning, NotificationSource.Git);
            return;
        }

        _notificationService.Notify("Gerando mensagem de commit com IA...", NotificationType.Progress, NotificationSource.Git);

        // 1. Delegate diff retrieval + prompt building to the dedicated service
        string aiMessage;
        try
        {
            aiMessage = await _gitAiService.GenerateCommitMessageAsync(CurrentProject.Path);
        }
        catch (Exception ex)
        {
            _notificationService.Notify($"Erro ao gerar mensagem: {ex.Message}", NotificationType.Error, NotificationSource.Git);
            return;
        }

        // GenerateCommitMessageAsync returns string.Empty for empty diffs;
        // surface the "no changes" warning to the user.
        if (string.IsNullOrWhiteSpace(aiMessage))
        {
            _notificationService.Notify("Nenhuma alteração detectada para commitar.", NotificationType.Warning, NotificationSource.Git);
            return;
        }

        if (string.IsNullOrWhiteSpace(aiMessage))
        {
            _notificationService.Notify("A IA não retornou uma mensagem.", NotificationType.Error, NotificationSource.Git);
            return;
        }

        // 2. Stage all
        var stageResult = await _gitService.StageAllAsync(CurrentProject.Path);
        if (!stageResult.Success)
        {
            _notificationService.Notify(stageResult.ErrorMessage ?? "Falha ao fazer stage", NotificationType.Error, NotificationSource.Git);
            return;
        }

        // 3. Commit
        var commitResult = await _gitService.CommitAsync(CurrentProject.Path, aiMessage);
        if (!commitResult.Success)
        {
            _notificationService.Notify(commitResult.ErrorMessage ?? "Falha ao criar commit", NotificationType.Error, NotificationSource.Git);
            return;
        }

        _notificationService.Notify($"Commit: \"{aiMessage}\" — fazendo push...", NotificationType.Progress, NotificationSource.Git);

        // 4. Push via terminal so output is visible
        RunCommandRequested?.Invoke("git push");

        await RefreshAsync();
    }

    [RelayCommand]
    private void ResetClaudeUsage()
    {
        _claudeUsage.Reset();
    }

    [RelayCommand]
    private void GitPush()
    {
        if (CurrentProject == null) return;
        _notificationService.Notify(
            "Executando git push...",
            NotificationType.Progress,
            NotificationSource.Git);
        RunCommandRequested?.Invoke("git push");
    }

    [RelayCommand]
    private void GitCreatePr()
    {
        if (CurrentProject == null) return;
        _notificationService.Notify(
            "Abrindo criação de PR...",
            NotificationType.Progress,
            NotificationSource.Git);
        RunCommandRequested?.Invoke("gh pr create");
    }

    [RelayCommand]
    private void OpenInCursor()
    {
        if (CurrentProject == null) return;
        _externalEditorService.Open(CurrentProject.Path, ExternalEditor.Cursor);
    }

    [RelayCommand]
    private void OpenInVsCode()
    {
        if (CurrentProject == null) return;
        _externalEditorService.Open(CurrentProject.Path, ExternalEditor.VsCode);
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (CurrentProject == null) return;
        _externalEditorService.Open(CurrentProject.Path, ExternalEditor.Explorer);
    }

    /// <summary>
    /// Requests the branch selector overlay to open (delegated to MainViewModel).
    /// </summary>
    [RelayCommand]
    private void OpenBranchSelector() => OpenBranchSelectorRequested?.Invoke();

    /// <summary>
    /// Loads and opens the worktree selector popup for the current project.
    /// </summary>
    [RelayCommand]
    private async Task OpenWorktreeSelector()
    {
        if (CurrentProject == null) return;
        await WorktreeSelector.LoadAsync(CurrentProject.Path);
        WorktreeSelector.IsOpen = true;
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
        _claudeUsage.UsageUpdated -= RefreshClaudeStats;
        _refreshTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
