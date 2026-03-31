using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Service for interacting with Git repositories.
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Gets the full Git info for a repository path.
    /// </summary>
    Task<GitInfo?> GetGitInfoAsync(string repositoryPath);

    /// <summary>
    /// Gets the current branch name.
    /// </summary>
    Task<string?> GetCurrentBranchAsync(string repositoryPath);

    /// <summary>
    /// Checks if the given path is inside a Git repository.
    /// </summary>
    Task<bool> IsGitRepositoryAsync(string path);

    /// <summary>
    /// Gets a list of modified files.
    /// </summary>
    Task<List<string>> GetModifiedFilesAsync(string repositoryPath);

    /// <summary>
    /// Gets a list of staged files.
    /// </summary>
    Task<List<string>> GetStagedFilesAsync(string repositoryPath);

    /// <summary>
    /// Gets the most recent commits.
    /// </summary>
    Task<List<GitCommitInfo>> GetRecentCommitsAsync(string repositoryPath, int count = 5);

    /// <summary>
    /// Gets the list of changed files in the working tree.
    /// </summary>
    Task<List<GitFileChange>> GetChangedFilesAsync(string repositoryPath);

    /// <summary>Gets all branches (local and remote).</summary>
    Task<List<GitBranchInfo>> GetBranchesAsync(string repositoryPath);

    /// <summary>Checks out a branch. For remote-only branches, creates a local tracking branch.</summary>
    Task<GitOperationResult> CheckoutBranchAsync(string repositoryPath, string branchName, bool isRemote = false);

    /// <summary>Returns true if there are any uncommitted changes.</summary>
    Task<bool> HasUncommittedChangesAsync(string repositoryPath);

    /// <summary>Lists all worktrees for the repository.</summary>
    Task<List<GitWorktreeInfo>> GetWorktreesAsync(string repositoryPath);

    /// <summary>Creates a new worktree at the specified path with the given branch.</summary>
    Task<GitOperationResult> CreateWorktreeAsync(string repositoryPath, string worktreePath, string branchName);

    /// <summary>Removes a worktree by its path.</summary>
    Task<GitOperationResult> RemoveWorktreeAsync(string worktreePath);

    /// <summary>Returns the full working-tree diff (unstaged + staged). Useful for AI commit message generation.</summary>
    Task<string> GetFullDiffAsync(string repositoryPath);

    /// <summary>Stages all changes (git add -A).</summary>
    Task<GitOperationResult> StageAllAsync(string repositoryPath);

    /// <summary>Creates a commit with the given message. Assumes files are already staged.</summary>
    Task<GitOperationResult> CommitAsync(string repositoryPath, string message);

    /// <summary>Pushes the current branch to its upstream remote.</summary>
    Task<GitOperationResult> PushAsync(string repositoryPath);

    /// <summary>Invalidates the cached git info for the given repository path.</summary>
    void InvalidateCache(string repositoryPath);
}
