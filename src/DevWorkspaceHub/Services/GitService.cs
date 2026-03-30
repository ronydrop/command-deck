using System.Diagnostics;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Executes git commands and parses output to populate GitInfo models.
/// </summary>
public class GitService : IGitService
{
    private const int CommandTimeoutMs = 5000;

    public async Task<GitInfo?> GetGitInfoAsync(string repositoryPath)
    {
        if (!await IsGitRepositoryAsync(repositoryPath))
            return null;

        var gitInfo = new GitInfo();

        // Get current branch
        var branchOutput = await RunGitCommandAsync(repositoryPath, "branch --show-current");
        if (branchOutput != null)
        {
            var branch = branchOutput.Trim();
            if (string.IsNullOrEmpty(branch))
            {
                // Detached HEAD state
                gitInfo.IsDetachedHead = true;
                var headRef = await RunGitCommandAsync(repositoryPath, "rev-parse --short HEAD");
                gitInfo.Branch = headRef?.Trim() ?? "HEAD";
            }
            else
            {
                gitInfo.Branch = branch;
            }
        }

        // Get last commit info: hash, message, relative time
        var logOutput = await RunGitCommandAsync(repositoryPath, "log -1 --format=\"%h|%s|%cr\"");
        if (logOutput != null)
        {
            var parts = logOutput.Trim().Trim('"').Split('|', 3);
            if (parts.Length >= 3)
            {
                gitInfo.LastCommitHash = parts[0];
                gitInfo.LastCommitMessage = parts[1];
                gitInfo.LastCommitRelativeTime = parts[2];
            }
        }

        // Get status (porcelain v1 for easy parsing)
        var statusOutput = await RunGitCommandAsync(repositoryPath, "status --porcelain");
        if (statusOutput != null)
        {
            ParseStatusOutput(statusOutput, gitInfo);
        }

        // Get ahead/behind
        var aheadBehind = await RunGitCommandAsync(repositoryPath,
            "rev-list --left-right --count @{upstream}...HEAD");
        if (aheadBehind != null)
        {
            var parts = aheadBehind.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                if (int.TryParse(parts[0], out int behind)) gitInfo.Behind = behind;
                if (int.TryParse(parts[1], out int ahead)) gitInfo.Ahead = ahead;
            }
        }

        // Check for stash
        var stashOutput = await RunGitCommandAsync(repositoryPath, "stash list");
        gitInfo.HasStash = !string.IsNullOrWhiteSpace(stashOutput);

        // Get remote
        var remoteOutput = await RunGitCommandAsync(repositoryPath, "remote");
        if (remoteOutput != null)
        {
            gitInfo.Remote = remoteOutput.Trim().Split('\n').FirstOrDefault() ?? "origin";
        }

        // Determine overall status
        gitInfo.RepoStatus = DetermineRepoStatus(gitInfo);

        return gitInfo;
    }

    public async Task<string?> GetCurrentBranchAsync(string repositoryPath)
    {
        var output = await RunGitCommandAsync(repositoryPath, "branch --show-current");
        return output?.Trim();
    }

    public async Task<bool> IsGitRepositoryAsync(string path)
    {
        var output = await RunGitCommandAsync(path, "rev-parse --is-inside-work-tree");
        return output?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    public async Task<List<string>> GetModifiedFilesAsync(string repositoryPath)
    {
        var output = await RunGitCommandAsync(repositoryPath, "diff --name-only");
        if (string.IsNullOrWhiteSpace(output)) return new();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task<List<string>> GetStagedFilesAsync(string repositoryPath)
    {
        var output = await RunGitCommandAsync(repositoryPath, "diff --cached --name-only");
        if (string.IsNullOrWhiteSpace(output)) return new();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    // ─── Private Methods ────────────────────────────────────────────────────

    /// <summary>
    /// Parses 'git status --porcelain' output to count file states.
    /// Format: XY filename
    /// X = staging area status, Y = working tree status
    /// </summary>
    private static void ParseStatusOutput(string output, GitInfo gitInfo)
    {
        int modified = 0, staged = 0, untracked = 0, conflicted = 0;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Length < 2) continue;

            char x = line[0]; // staging area
            char y = line[1]; // working tree

            // Conflicted
            if (x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D'))
            {
                conflicted++;
                continue;
            }

            // Untracked
            if (x == '?' && y == '?')
            {
                untracked++;
                continue;
            }

            // Staged changes (index column)
            if (x is 'A' or 'M' or 'D' or 'R' or 'C')
                staged++;

            // Working tree changes
            if (y is 'M' or 'D')
                modified++;
        }

        gitInfo.ModifiedFiles = modified;
        gitInfo.StagedFiles = staged;
        gitInfo.UntrackedFiles = untracked;
        gitInfo.ConflictedFiles = conflicted;
    }

    private static GitRepoStatus DetermineRepoStatus(GitInfo info)
    {
        if (info.IsDetachedHead) return GitRepoStatus.Detached;
        if (info.ConflictedFiles > 0) return GitRepoStatus.Conflicted;
        if (info.StagedFiles > 0) return GitRepoStatus.Staged;
        if (info.ModifiedFiles > 0 || info.UntrackedFiles > 0) return GitRepoStatus.Modified;
        return GitRepoStatus.Clean;
    }

    /// <summary>
    /// Runs a git command and returns stdout, or null on failure.
    /// </summary>
    private static async Task<string?> RunGitCommandAsync(string workingDirectory, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();

            using var cts = new CancellationTokenSource(CommandTimeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
