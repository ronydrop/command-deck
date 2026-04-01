using System;
using System.Threading;
using System.Threading.Tasks;

namespace CommandDeck.Services;

/// <summary>
/// Generates AI-powered commit messages by combining git diff context
/// with the active assistant provider.
/// </summary>
public sealed class GitAiService : IGitAiService
{
    private readonly IGitService _gitService;
    private readonly IAssistantService _assistantService;

    /// <summary>
    /// Initializes a new instance of <see cref="GitAiService"/>.
    /// </summary>
    public GitAiService(IGitService gitService, IAssistantService assistantService)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _assistantService = assistantService ?? throw new ArgumentNullException(nameof(assistantService));
    }

    /// <inheritdoc/>
    public async Task<string> GenerateCommitMessageAsync(string repoPath, CancellationToken ct = default)
    {
        if (!_assistantService.IsAnyProviderAvailable)
            throw new InvalidOperationException("No AI provider is available.");

        var diff = await _gitService.GetFullDiffAsync(repoPath);
        if (string.IsNullOrWhiteSpace(diff))
            return string.Empty;

        // Build a self-contained prompt so the assistant understands the task
        // regardless of the generic system prompt used by ExplainTerminalOutputAsync.
        var prompt =
            "You are an expert developer. Based on the following git diff, write a concise, " +
            "imperative commit message (max 72 chars). Reply with ONLY the commit message, no explanations.\n\n" +
            diff;

        var raw = await _assistantService.ExplainTerminalOutputAsync(prompt, ct);
        return raw.Trim().Trim('"').Trim();
    }
}
