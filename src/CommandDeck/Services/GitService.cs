using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Executes git commands and parses output to populate GitInfo models.
/// Caches GetGitInfoAsync results per repository path for 10 seconds to avoid
/// redundant git process spawns during project switches.
/// </summary>
public class GitService : IGitService
{
    private const int CommandTimeoutMs = 5000;
    private static readonly TimeSpan GitInfoCacheDuration = TimeSpan.FromSeconds(10);

    // Per-path cache of GitInfo results using Lazy to prevent duplicate git spawns
    private readonly ConcurrentDictionary<string, Lazy<Task<(GitInfo? Info, DateTime Timestamp)>>> _gitInfoCache = new();

    // Simple TTL caches for secondary git queries (same 10s window as GitInfo)
    private readonly ConcurrentDictionary<string, (List<GitCommitInfo> Data, DateTime Expiry)> _commitsCache = new();
    private readonly ConcurrentDictionary<string, (List<GitFileChange> Data, DateTime Expiry)> _changesCache = new();

    public async Task<GitInfo?> GetGitInfoAsync(string repositoryPath)
    {
        var normalizedPath = Path.GetFullPath(repositoryPath);

        var lazyEntry = _gitInfoCache.GetOrAdd(normalizedPath, path => new Lazy<Task<(GitInfo? Info, DateTime Timestamp)>>(
            () => FetchGitInfoCoreAsync(path)));

        var entry = await lazyEntry.Value;
        if (DateTime.UtcNow - entry.Timestamp < GitInfoCacheDuration)
            return entry.Info;

        // Cache expired - remove and re-fetch
        _gitInfoCache.TryRemove(normalizedPath, out _);
        lazyEntry = _gitInfoCache.GetOrAdd(normalizedPath, path => new Lazy<Task<(GitInfo? Info, DateTime Timestamp)>>(
            () => FetchGitInfoCoreAsync(path)));
        entry = await lazyEntry.Value;
        return entry.Info;
    }

    /// <summary>
    /// Core method that spawns git processes and builds a GitInfo result.
    /// Called exclusively through the Lazy cache to prevent duplicate spawns.
    /// </summary>
    private async Task<(GitInfo? Info, DateTime Timestamp)> FetchGitInfoCoreAsync(string normalizedPath)
    {
        if (!await IsGitRepositoryAsync(normalizedPath))
        {
            return (null, DateTime.UtcNow);
        }

        var gitInfo = new GitInfo();

        // Run all independent git commands in parallel
        var branchTask = RunGitCommandAsync(normalizedPath, "branch --show-current");
        var logTask = RunGitCommandAsync(normalizedPath, "log -1 --format=\"%h|%s|%cr\"");
        var statusTask = RunGitCommandAsync(normalizedPath, "status --porcelain");
        var aheadBehindTask = RunGitCommandAsync(normalizedPath, "rev-list --left-right --count @{upstream}...HEAD");
        var stashTask = RunGitCommandAsync(normalizedPath, "stash list");
        var remoteTask = RunGitCommandAsync(normalizedPath, "remote");

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
                var headRef = await RunGitCommandAsync(normalizedPath, "rev-parse --short HEAD");
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

        return (gitInfo, DateTime.UtcNow);
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
        var key = Path.GetFullPath(repositoryPath);
        if (_commitsCache.TryGetValue(key, out var cached) && DateTime.UtcNow < cached.Expiry)
            return cached.Data;

        var commits = new List<GitCommitInfo>();
        var output = await RunGitCommandAsync(repositoryPath, $"log -{count} --format=\"%h|%s|%an|%cr\"");
        if (string.IsNullOrWhiteSpace(output))
        {
            _commitsCache[key] = (commits, DateTime.UtcNow + GitInfoCacheDuration);
            return commits;
        }

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
        _commitsCache[key] = (commits, DateTime.UtcNow + GitInfoCacheDuration);
        return commits;
    }

    public async Task<List<GitFileChange>> GetChangedFilesAsync(string repositoryPath)
    {
        var key = Path.GetFullPath(repositoryPath);
        if (_changesCache.TryGetValue(key, out var cached) && DateTime.UtcNow < cached.Expiry)
            return cached.Data;

        var changes = new List<GitFileChange>();
        var output = await RunGitCommandAsync(repositoryPath, "status --porcelain");
        if (string.IsNullOrWhiteSpace(output))
        {
            _changesCache[key] = (changes, DateTime.UtcNow + GitInfoCacheDuration);
            return changes;
        }

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
        _changesCache[key] = (changes, DateTime.UtcNow + GitInfoCacheDuration);
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
    /// Runs a git command and returns stdout, or null on failure/non-zero exit.
    /// Delegates to <see cref="RunGitCommandFullAsync"/> for the actual process spawn.
    /// </summary>
    private static async Task<string?> RunGitCommandAsync(string workingDirectory, string arguments)
    {
        var (stdout, _, exitCode) = await RunGitCommandFullAsync(workingDirectory, arguments);
        return exitCode == 0 ? stdout : null;
    }

    /// <summary>
    /// Runs a git command and returns stdout, stderr, and the exit code.
    /// On timeout the process is killed and exit code -1 is returned.
    /// </summary>
    private static async Task<(string? Stdout, string? Stderr, int ExitCode)> RunGitCommandFullAsync(
        string workingDirectory, string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
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
                return (null, "Command timed out", -1);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (stdout, stderr, process.ExitCode);
        }
        catch (Exception ex)
        {
            return (null, ex.Message, -1);
        }
    }

    // ─── New Methods ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<GitBranchInfo>> GetBranchesAsync(string repositoryPath)
    {
        var output = await RunGitCommandAsync(repositoryPath, @"branch -a --format=%(refname:short)|%(HEAD)");
        if (output == null) return [];

        var branches = new List<GitBranchInfo>();
        var localNames = new HashSet<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            var refName = parts[0].Trim();
            var isCurrent = parts[1].Trim() == "*";

            if (refName.StartsWith("remotes/", StringComparison.Ordinal))
            {
                // e.g. "remotes/origin/main" → remote=origin, display=main
                var withoutRemotes = refName[8..]; // strip "remotes/"
                var slashIdx = withoutRemotes.IndexOf('/');
                if (slashIdx < 0) continue;
                var remoteName = withoutRemotes[..slashIdx];
                var displayName = withoutRemotes[(slashIdx + 1)..];
                if (displayName == "HEAD") continue; // skip remote HEAD pointer

                branches.Add(new GitBranchInfo
                {
                    Name = refName,
                    DisplayName = displayName,
                    IsRemote = true,
                    IsCurrent = isCurrent,
                    RemoteName = remoteName
                });
            }
            else
            {
                localNames.Add(refName);
                branches.Add(new GitBranchInfo
                {
                    Name = refName,
                    DisplayName = refName,
                    IsRemote = false,
                    IsCurrent = isCurrent
                });
            }
        }

        // Remove remote branches that have a corresponding local branch
        branches.RemoveAll(b => b.IsRemote && localNames.Contains(b.DisplayName));

        // Sort: current first, then local, then remote
        branches.Sort((a, b) =>
        {
            if (a.IsCurrent != b.IsCurrent) return a.IsCurrent ? -1 : 1;
            if (a.IsRemote != b.IsRemote) return a.IsRemote ? 1 : -1;
            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        return branches;
    }

    /// <inheritdoc/>
    public async Task<GitOperationResult> CheckoutBranchAsync(string repositoryPath, string branchName, bool isRemote = false)
    {
        if (isRemote)
        {
            // branchName is the display name (e.g. "feature-x"); create a local tracking branch
            var (_, stderr1, exitCode1) = await RunGitCommandFullAsync(repositoryPath, $"checkout -b {branchName} origin/{branchName}");
            if (exitCode1 != 0)
                return GitOperationResult.Fail(stderr1 ?? "Erro ao fazer checkout da branch remota");
            return GitOperationResult.Ok();
        }

        var (_, stderr, exitCode) = await RunGitCommandFullAsync(repositoryPath, $"checkout {branchName}");
        if (exitCode != 0)
            return GitOperationResult.Fail(stderr ?? "Erro ao trocar de branch");
        return GitOperationResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<bool> HasUncommittedChangesAsync(string repositoryPath)
    {
        var output = await RunGitCommandAsync(repositoryPath, "status --porcelain");
        return !string.IsNullOrWhiteSpace(output);
    }

    /// <inheritdoc/>
    public async Task<List<GitWorktreeInfo>> GetWorktreesAsync(string repositoryPath)
    {
        var output = await RunGitCommandAsync(repositoryPath, "worktree list --porcelain");
        if (output == null) return [];

        var result = new List<GitWorktreeInfo>();
        var isFirst = true;

        foreach (var block in output.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var info = new GitWorktreeInfo { IsMain = isFirst };
            isFirst = false;

            foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("worktree ")) info.Path = line[9..].Trim();
                else if (line.StartsWith("HEAD ")) info.CommitHash = line[5..7]; // short hash
                else if (line.StartsWith("branch refs/heads/")) info.Branch = line[18..].Trim();
                else if (line == "bare") info.IsBare = true;
                else if (line == "detached") info.Branch = "(detached)";
            }

            if (!string.IsNullOrEmpty(info.Path))
                result.Add(info);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<GitOperationResult> CreateWorktreeAsync(string repositoryPath, string worktreePath, string branchName)
    {
        var (_, stderr, exitCode) = await RunGitCommandFullAsync(repositoryPath, $"worktree add \"{worktreePath}\" {branchName}");
        if (exitCode != 0)
            return GitOperationResult.Fail(stderr ?? "Erro ao criar worktree");
        return GitOperationResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<GitOperationResult> RemoveWorktreeAsync(string worktreePath)
    {
        // worktree remove is run from the parent directory; git resolves the main repo automatically
        var parentDir = System.IO.Path.GetDirectoryName(worktreePath) ?? worktreePath;
        var (_, stderr, exitCode) = await RunGitCommandFullAsync(parentDir, $"worktree remove \"{worktreePath}\"");
        if (exitCode != 0)
            return GitOperationResult.Fail(stderr ?? "Erro ao remover worktree");
        return GitOperationResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<string> GetFullDiffAsync(string repositoryPath)
    {
        // Combine staged diff + unstaged diff for maximum context to the AI
        var staged   = await RunGitCommandAsync(repositoryPath, "diff --cached") ?? string.Empty;
        var unstaged = await RunGitCommandAsync(repositoryPath, "diff") ?? string.Empty;
        var combined = (staged + "\n" + unstaged).Trim();

        // Truncate to ~12 000 chars so we don't blow the AI context window
        const int MaxChars = 12_000;
        return combined.Length > MaxChars ? combined[..MaxChars] + "\n... (diff truncated)" : combined;
    }

    /// <inheritdoc/>
    public async Task<GitOperationResult> StageAllAsync(string repositoryPath)
    {
        var (_, stderr, exitCode) = await RunGitCommandFullAsync(repositoryPath, "add -A");
        if (exitCode != 0)
            return GitOperationResult.Fail(stderr ?? "Erro ao fazer stage dos arquivos");
        return GitOperationResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<GitOperationResult> CommitAsync(string repositoryPath, string message)
    {
        // Escape double quotes in the message to avoid shell injection
        var safeMessage = message.Replace("\"", "\\\"");
        var (_, stderr, exitCode) = await RunGitCommandFullAsync(repositoryPath, $"commit -m \"{safeMessage}\"");
        if (exitCode != 0)
            return GitOperationResult.Fail(stderr ?? "Erro ao criar commit");
        InvalidateCache(repositoryPath);
        return GitOperationResult.Ok();
    }

    /// <inheritdoc/>
    public async Task<GitOperationResult> PushAsync(string repositoryPath)
    {
        var (_, stderr, exitCode) = await RunGitCommandFullAsync(repositoryPath, "push");
        if (exitCode != 0)
            return GitOperationResult.Fail(stderr ?? "Erro ao fazer push");
        InvalidateCache(repositoryPath);
        return GitOperationResult.Ok();
    }

    /// <inheritdoc/>
    public void InvalidateCache(string repositoryPath)
    {
        var key = Path.GetFullPath(repositoryPath); // same normalization as GetGitInfoAsync
        _gitInfoCache.TryRemove(key, out _);
        _commitsCache.TryRemove(key, out _);
        _changesCache.TryRemove(key, out _);
    }
}
