namespace CommandDeck.Models;

/// <summary>
/// A tool call requested by the AI assistant during a generation turn.
/// Maps to Anthropic's <c>tool_use</c> content block and OpenAI's <c>tool_calls</c> array item.
/// </summary>
public sealed class ToolCall
{
    /// <summary>
    /// Unique call identifier assigned by the provider.
    /// Anthropic: <c>tool_use_id</c>. OpenAI: <c>tool_calls[i].id</c>.
    /// Must be echoed back in the <see cref="AssistantMessage.ToolCallId"/> of the tool result.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Name of the tool being invoked (matches <see cref="ToolDefinition.Name"/>).</summary>
    public required string Name { get; init; }

    /// <summary>Raw JSON string containing the tool's input arguments.</summary>
    public required string InputJson { get; init; }
}
