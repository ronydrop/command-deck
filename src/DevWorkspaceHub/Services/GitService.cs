using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Executes git commands and parses output to populate GitInfo models.
/// Caches GetGitInfoAsync results per repository path for 10 seconds to avoid
/// redundant git process spawns during project switches.
/// </summary>
public class GitService : IGitService
{
    private const int CommandTimeoutMs = 5000;
    private static readonly TimeSpan GitInfoCacheDuration = TimeSpan.FromSeconds(10);

    // Per-path cache of GitInfo results to avoid redundant spawns during rapid calls
    private readonly ConcurrentDictionary<string, (GitInfo? Info, DateTime Timestamp)> _gitInfoCache = new();

    public async Task<GitInfo?> GetGitInfoAsync(string repositoryPath)
    {
        // Return cached result if still fresh
        var normalizedPath = Path.GetFullPath(repositoryPath);
        if (_gitInfoCache.TryGetValue(normalizedPath, out var cached)
            && DateTime.UtcNow - cached.Timestamp < GitInfoCacheDuration)
        {
            return cached.Info;
        }

        if (!await IsGitRepositoryAsync(repositoryPath))
        {
            _gitInfoCache[normalizedPath] = (null, DateTime.UtcNow);
            return null;
        }

        var gitInfo = new GitInfo();

        // Run all independent git commands in parallel
        var branchTask = RunGitCommandAsync(repositoryPath, "branch --show-current");
        var logTask = RunGitCommandAsync(repositoryPath, "log -1 --format=\"%h|%s|%cr\"");
        var statusTask = RunGitCommandAsync(repositoryPath, "status --porcelain");
        var aheadBehindTask = RunGitCommandAsync(repositoryPath, "rev-list --left-right --count @{upstream}...HEAD");
        var stashTask = RunGitCommandAsync(repositoryPath, "stash list");
        var remoteTask = RunGitCommandAsync(repositoryPath, "remote");

        await Task.WhenAll(branchTask, logTask, statusTask, aheadBehindTask, stashTask, remoteTask);

        // Parse branch
        var branchOutput = branchTask.Result;
        if (branchOutput != null)
        {
            var branch = branchOutput.Trim();
            if (string.IsNullOrEmpty(branch))
            {
                // Detached HEAD state — need a follow-up call
                gitInfo.IsDetachedHead = true;
                var headRef = await RunGitCommandAsync(repositoryPath, "rev-parse --short HEAD");
                gitInfo.Branch = headRef?.Trim() ?? "HEAD";
            }
            else
            {
                gitInfo.Branch = branch;
            }
        }

        // Parse last commit info
        var logOutput = logTask.Result;
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

        // Parse status
        var statusOutput = statusTask.Result;
        if (statusOutput != null)
        {
            ParseStatusOutput(statusOutput, gitInfo);
        }

        // Parse ahead/behind
        var aheadBehind = aheadBehindTask.Result;
        if (aheadBehind != null)
        {
            var parts = aheadBehind.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                if (int.TryParse(parts[0], out int behind)) gitInfo.Behind = behind;
                if (int.TryParse(parts[1], out int ahead)) gitInfo.Ahead = ahead;
            }
        }

        // Parse stash
        gitInfo.HasStash = !string.IsNullOrWhiteSpace(stashTask.Result);

        // Parse remote
        var remoteOutput = remoteTask.Result;
        if (remoteOutput != null)
        {
            gitInfo.Remote = remoteOutput.Trim().Split('\n').FirstOrDefault() ?? "origin";
        }

        // Determine overall status
        gitInfo.RepoStatus = DetermineRepoStatus(gitInfo);

        // Cache the result
        _gitInfoCache[normalizedPath] = (gitInfo, DateTime.UtcNow);

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

    public async Task<List<GitCommitInfo>> GetRecentCommitsAsync(string repositoryPath, int count = 5)
    {
        var commits = new List<GitCommitInfo>();
        var output = await RunGitCommandAsync(repositoryPath, $"log -{count} --format=\"%h|%s|%an|%cr\"");
        if (string.IsNullOrWhiteSpace(output)) return commits;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Trim('"').Split('|', 4);
            if (parts.Length >= 4)
            {
                commits.Add(new GitCommitInfo
                {
                    Hash = parts[0],
                    Message = parts[1],
                    Author = parts[2],
                    RelativeTime = parts[3]
                });
            }
        }
        return commits;
    }

    public async Task<List<GitFileChange>> GetChangedFilesAsync(string repositoryPath)
    {
        var changes = new List<GitFileChange>();
        var output = await RunGitCommandAsync(repositoryPath, "status --porcelain");
        if (string.IsNullOrWhiteSpace(output)) return changes;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3) continue;
            char x = line[0];
            char y = line[1];
            var filePath = line[3..].Trim();

            var status = (x, y) switch
            {
                ('?', '?') => "?",
                ('U', _) or (_, 'U') => "U",
                ('A', _) or (_, 'A') when x != '?' => "A",
                ('D', _) or (_, 'D') => "D",
                ('R', _) => "R",
                _ when (x == 'M' || y == 'M') => "M",
                _ => "M"
            };

            var statusDisplay = status switch
            {
                "M" => "Modificado",
                "A" => "Adicionado",
                "D" => "Removido",
                "R" => "Renomeado",
                "?" => "Não rastreado",
                "U" => "Conflito",
                _ => "Alterado"
            };

            changes.Add(new GitFileChange
            {
                FilePath = filePath,
                Status = status,
                StatusDisplay = statusDisplay
            });
        }
        return changes;
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

            // Read stdout and stderr concurrently to prevent buffer deadlocks
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

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

            var output = await stdoutTask;
            await stderrTask; // consume stderr to avoid buffer issues

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
