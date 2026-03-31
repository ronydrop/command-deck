using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CommandDeck.Models;

/// <summary>
/// Maps known AI model identifiers to their maximum context window in tokens.
/// Used to automatically trim conversation history to fit each model's limit.
/// </summary>
public static class ModelContextWindows
{
    private static readonly Dictionary<string, int> _windows = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Anthropic Claude ──────────────────────────────────────────────────
        ["claude-opus-4-6"]           = 200_000,
        ["claude-sonnet-4-6"]         = 200_000,
        ["claude-opus-4"]             = 200_000,
        ["claude-sonnet-4"]           = 200_000,
        ["claude-haiku-4-5"]          = 200_000,
        ["claude-3-5-sonnet"]         = 200_000,
        ["claude-3-5-haiku"]          = 200_000,
        ["claude-3-opus"]             = 200_000,
        ["claude-3-sonnet"]           = 200_000,
        ["claude-3-haiku"]            = 200_000,

        // ── OpenAI ────────────────────────────────────────────────────────────
        ["gpt-4o"]          = 128_000,
        ["gpt-4o-mini"]     = 128_000,
        ["gpt-4-turbo"]     = 128_000,
        ["gpt-4"]           = 8_192,
        ["gpt-3.5-turbo"]   = 16_385,
        ["o3-mini"]         = 200_000,
        ["o1"]              = 200_000,
        ["o1-mini"]         = 128_000,

        // ── Ollama / local common models ──────────────────────────────────────
        ["llama3.2"]     = 128_000,
        ["llama3.1"]     = 128_000,
        ["llama3"]       = 8_192,
        ["mistral"]      = 32_768,
        ["gemma2"]       = 8_192,
        ["qwen2.5"]      = 128_000,
        ["phi3"]         = 128_000,
        ["deepseek-r1"]  = 64_000,
        ["deepseek-v3"]  = 64_000,
        ["codellama"]    = 16_384,
    };

    /// <summary>
    /// Tokens reserved for the model's response. Not consumed by the conversation history.
    /// </summary>
    public const int ResponseReserveTokens = 4_096;

    /// <summary>
    /// Default context window used when the model is unknown.
    /// Conservative 128k covers most modern models.
    /// </summary>
    public const int DefaultContextWindow = 128_000;

    /// <summary>
    /// Returns the context window in tokens for <paramref name="model"/>.
    /// Performs a prefix/suffix match so versioned names like
    /// "claude-sonnet-4-6-20250627" or "anthropic/claude-sonnet-4.6" resolve correctly.
    /// </summary>
    public static int Get(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return DefaultContextWindow;

        // Normalize: strip OpenRouter-style "provider/" prefix and trailing version dates
        var normalized = model.Contains('/') ? model[(model.LastIndexOf('/') + 1)..] : model;

        // Exact match first
        if (_windows.TryGetValue(normalized, out var exact)) return exact;

        // Prefix match: "claude-sonnet-4-6-20250627" starts with "claude-sonnet-4-6"
        foreach (var (key, tokens) in _windows)
            if (normalized.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                return tokens;

        return DefaultContextWindow;
    }
}

// ─── Ollama API request/response models ─────────────────────────────────────

public class OllamaRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("messages")]
    public List<OllamaMessage> Messages { get; set; } = new();
}

public class OllamaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Top-level response wrapper from /api/chat (both streaming and non-streaming).
/// </summary>
public class OllamaResponse
{
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    /// <summary>Shortcut to the inner message content.</summary>
    [JsonIgnore]
    public string Content => Message?.Content ?? string.Empty;
}

// ─── Assistant configuration ─────────────────────────────────────────────────

public enum AssistantProviderType
{
    None,
    Ollama,
    OpenAI,
    Anthropic,
    OpenRouter
}

public enum AnthropicAuthMode
{
    ApiKey,
    ClaudeOAuth
}

public class AssistantSettings
{
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = string.Empty;
    public string? OpenAIKey { get; set; }
    public string OpenAIModel { get; set; } = string.Empty;
    public string? AnthropicKey { get; set; }
    public string AnthropicModel { get; set; } = "claude-sonnet-4-6";
    public string? OpenRouterKey { get; set; }
    public string OpenRouterModel { get; set; } = "anthropic/claude-sonnet-4.6";
    public AnthropicAuthMode AnthropicAuth { get; set; } = AnthropicAuthMode.ApiKey;
    public AssistantProviderType ActiveProvider { get; set; } = AssistantProviderType.None;
}
