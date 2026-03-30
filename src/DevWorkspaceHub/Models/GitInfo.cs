using CommunityToolkit.Mvvm.ComponentModel;

namespace DevWorkspaceHub.Models;

/// <summary>
/// Represents Git repository information for a project.
/// </summary>
public partial class GitInfo : ObservableObject
{
    [ObservableProperty]
    private string _branch = string.Empty;

    [ObservableProperty]
    private string _remote = "origin";

    [ObservableProperty]
    private string _lastCommitHash = string.Empty;

    [ObservableProperty]
    private string _lastCommitMessage = string.Empty;

    [ObservableProperty]
    private string _lastCommitRelativeTime = string.Empty;

    [ObservableProperty]
    private int _modifiedFiles;

    [ObservableProperty]
    private int _stagedFiles;

    [ObservableProperty]
    private int _untrackedFiles;

    [ObservableProperty]
    private int _conflictedFiles;

    [ObservableProperty]
    private int _ahead;

    [ObservableProperty]
    private int _behind;

    [ObservableProperty]
    private bool _hasStash;

    [ObservableProperty]
    private bool _isDetachedHead;

    [ObservableProperty]
    private GitRepoStatus _repoStatus = GitRepoStatus.Clean;

    /// <summary>
    /// Gets a summary status text.
    /// </summary>
    public string StatusSummary
    {
        get
        {
            var parts = new List<string>();
            if (StagedFiles > 0) parts.Add($"+{StagedFiles} staged");
            if (ModifiedFiles > 0) parts.Add($"~{ModifiedFiles} modified");
            if (UntrackedFiles > 0) parts.Add($"?{UntrackedFiles} untracked");
            if (ConflictedFiles > 0) parts.Add($"!{ConflictedFiles} conflicts");
            return parts.Count > 0 ? string.Join(", ", parts) : "Clean";
        }
    }

    /// <summary>
    /// Gets branch info with ahead/behind indicators.
    /// </summary>
    public string BranchDisplay
    {
        get
        {
            var display = IsDetachedHead ? $"({LastCommitHash})" : Branch;
            if (Ahead > 0) display += $" ↑{Ahead}";
            if (Behind > 0) display += $" ↓{Behind}";
            return display;
        }
    }
}

/// <summary>
/// Overall status of the git repository.
/// </summary>
public enum GitRepoStatus
{
    Clean,
    Modified,
    Staged,
    Conflicted,
    Detached,
    Unknown
}
