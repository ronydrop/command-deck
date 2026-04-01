using System.Threading;
using System.Threading.Tasks;

namespace CommandDeck.Services;

/// <summary>
/// Generates AI-powered commit messages from git diffs.
/// </summary>
public interface IGitAiService
{
    /// <summary>
    /// Analyzes the full diff of the repository and returns a suggested commit message.
    /// Returns "chore: minor changes" when no diff is found.
    /// </summary>
    Task<string> GenerateCommitMessageAsync(string repoPath, CancellationToken ct = default);
}
