using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

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
}
