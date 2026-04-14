namespace CommandDeck.Models;

/// <summary>
/// The result of executing a <see cref="ToolCall"/>, to be fed back to the assistant.
/// </summary>
public sealed class ToolResult
{
    /// <summary>
    /// The <see cref="ToolCall.Id"/> of the call that produced this result.
    /// Must be included when sending the result back to the provider.
    /// </summary>
    public required string ToolCallId { get; init; }

    /// <summary>Stringified result content (JSON, plain text, or error message).</summary>
    public required string Content { get; init; }

    /// <summary>Whether the tool execution failed. The model will receive the error message.</summary>
    public bool IsError { get; init; }
}
