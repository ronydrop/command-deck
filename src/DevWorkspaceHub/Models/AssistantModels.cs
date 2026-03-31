using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DevWorkspaceHub.Models;

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
    public string AnthropicModel { get; set; } = "claude-sonnet-4-20250514";
    public string? OpenRouterKey { get; set; }
    public string OpenRouterModel { get; set; } = "anthropic/claude-sonnet-4";
    public AnthropicAuthMode AnthropicAuth { get; set; } = AnthropicAuthMode.ApiKey;
    public AssistantProviderType ActiveProvider { get; set; } = AssistantProviderType.None;
}
