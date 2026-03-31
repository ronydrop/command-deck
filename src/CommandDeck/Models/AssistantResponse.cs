namespace CommandDeck.Models;

/// <summary>
/// Reason why the assistant stopped generating.
/// </summary>
public enum FinishReason
{
    /// <summary>The model completed its response naturally (stop token or end of text).</summary>
    Stop,

    /// <summary>The response was truncated due to max token limit.</summary>
    Length,

    /// <summary>An error occurred during generation.</summary>
    Error,

    /// <summary>The model requested a tool call (not yet supported).</summary>
    ToolCalls,

    /// <summary>Reason is unknown or not provided by the API.</summary>
    Unknown
}

/// <summary>
/// Token usage statistics from an API response.
/// </summary>
public class TokenUsage
{
    /// <summary>Number of tokens in the prompt (input).</summary>
    public int PromptTokens { get; init; }

    /// <summary>Number of tokens in the completion (output).</summary>
    public int CompletionTokens { get; init; }

    /// <summary>Total tokens (prompt + completion).</summary>
    public int TotalTokens => PromptTokens + CompletionTokens;
}

/// <summary>
/// Response from an AI assistant provider.
/// </summary>
public class AssistantResponse
{
    /// <summary>The generated text content.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>The reason the generation stopped.</summary>
    public FinishReason FinishReason { get; init; } = FinishReason.Stop;

    /// <summary>Token usage statistics, if available.</summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>Error message, if the response indicates a failure.</summary>
    public string? Error { get; init; }

    /// <summary>Whether the response represents an error.</summary>
    public bool IsError => !string.IsNullOrEmpty(Error) || FinishReason == FinishReason.Error;

    /// <summary>
    /// Creates a successful response.
    /// </summary>
    public static AssistantResponse Success(string content, TokenUsage? usage = null) => new()
    {
        Content = content,
        FinishReason = FinishReason.Stop,
        Usage = usage
    };

    /// <summary>
    /// Creates an error response.
    /// </summary>
    public static AssistantResponse Failed(string error) => new()
    {
        Content = string.Empty,
        FinishReason = FinishReason.Error,
        Error = error
    };
}
