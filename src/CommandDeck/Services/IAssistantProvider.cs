using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Abstraction over an AI backend (OpenAI, Ollama, etc.).
/// Supports both legacy providers (OllamaProvider with old API)
/// and expanded providers (OpenAIProvider with new API).
/// </summary>
public interface IAssistantProvider
{
    // ─── Common members (both old and new must implement) ─────────────────

    /// <summary>Whether this provider is available on the current system.</summary>
    bool IsAvailable { get; }

    /// <summary>Legacy provider name. Default: falls back to Name.</summary>
    string ProviderName => Name;

    // ─── WSL-style members (with defaults for old providers) ──────────────

    /// <summary>Unique machine-readable identifier. Default: ProviderName.</summary>
    string Name => ProviderName;

    /// <summary>Human-readable display name. Default: ProviderName.</summary>
    string DisplayName => ProviderName;

    /// <summary>Hex color string for provider display (Catppuccin palette). Default: overlay0.</summary>
    string DisplayColor => "#6C7086";

    /// <summary>Whether configured with required credentials. Default: IsAvailable.</summary>
    bool IsConfigured => IsAvailable;

    /// <summary>Initialize provider. Default: no-op.</summary>
    Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Chat completion returning AssistantResponse. Default: not supported.</summary>
    Task<AssistantResponse> ChatAsync(IReadOnlyList<AssistantMessage> messages)
        => Task.FromResult(AssistantResponse.Failed("ChatAsync not implemented by this provider."));

    /// <summary>Streaming chat completion. Default: not supported.</summary>
    IAsyncEnumerable<AssistantResponse> StreamChatAsync(
        IReadOnlyList<AssistantMessage> messages,
        Action<string>? onChunk = null) => DefaultStreamChatFallback();

    /// <summary>Cancel current request. Default: no-op.</summary>
    void CancelCurrentRequest() { }

    // ─── Legacy WIN members (with defaults for new providers) ─────────────

    /// <summary>Explain terminal output. Default: delegates to ChatAsync.</summary>
    async Task<string> ExplainAsync(string terminalOutput, CancellationToken ct = default)
    {
        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Explain the following terminal output concisely."),
            AssistantMessage.User(terminalOutput)
        };
        var response = await ChatAsync(messages);
        return response.IsError ? $"AI Error: {response.Error}" : response.Content ?? string.Empty;
    }

    /// <summary>Suggest a shell command. Default: delegates to ChatAsync.</summary>
    async Task<string> SuggestCommandAsync(string description, string? shellHint = null, CancellationToken ct = default)
    {
        var shellPart = shellHint is not null ? $" Target shell: {shellHint}." : string.Empty;
        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Suggest a shell command. Reply with only the command."),
            AssistantMessage.User($"Task: {description}{shellPart}")
        };
        var response = await ChatAsync(messages);
        return response.IsError ? $"AI Error: {response.Error}" : response.Content ?? string.Empty;
    }

    // ─── Static helper for default StreamChatAsync ────────────────────────

    private static async IAsyncEnumerable<AssistantResponse> DefaultStreamChatFallback()
    {
        yield return await Task.FromResult(
            AssistantResponse.Failed("StreamChatAsync not implemented by this provider."));
    }
}
