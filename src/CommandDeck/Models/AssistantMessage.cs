namespace CommandDeck.Models;

/// <summary>
/// Role of the participant in an assistant conversation.
/// </summary>
public enum AssistantRole
{
    /// <summary>System instructions that set behavior and context.</summary>
    System,

    /// <summary>Human user input.</summary>
    User,

    /// <summary>AI assistant response.</summary>
    Assistant,

    /// <summary>Tool execution result fed back to the assistant.</summary>
    Tool
}

/// <summary>
/// A single message in the assistant conversation thread.
/// Maps to the OpenAI chat completion message format and Anthropic Messages API format.
/// </summary>
public class AssistantMessage
{
    /// <summary>The role of the message sender.</summary>
    public AssistantRole Role { get; init; } = AssistantRole.User;

    /// <summary>The text content of the message.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>UTC timestamp when this message was created.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Tool calls requested by the assistant in this turn.
    /// Only set when <see cref="Role"/> is <see cref="AssistantRole.Assistant"/> and
    /// the model requested tool use.
    /// </summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// The ID of the tool call this result belongs to.
    /// Only set when <see cref="Role"/> is <see cref="AssistantRole.Tool"/>.
    /// Matches <see cref="ToolCall.Id"/> from the preceding assistant message.
    /// </summary>
    public string? ToolCallId { get; init; }

    // ─── Factory helpers ──────────────────────────────────────────────────────

    /// <summary>Creates a system message for instructions.</summary>
    public static AssistantMessage System(string content) => new()
    {
        Role = AssistantRole.System,
        Content = content,
        Timestamp = DateTime.UtcNow
    };

    /// <summary>Creates a user message.</summary>
    public static AssistantMessage User(string content) => new()
    {
        Role = AssistantRole.User,
        Content = content,
        Timestamp = DateTime.UtcNow
    };

    /// <summary>Creates an assistant text response message.</summary>
    public static AssistantMessage Assistant(string content) => new()
    {
        Role = AssistantRole.Assistant,
        Content = content,
        Timestamp = DateTime.UtcNow
    };

    /// <summary>
    /// Creates an assistant message that includes tool calls alongside optional text content.
    /// </summary>
    public static AssistantMessage AssistantWithTools(string text, IReadOnlyList<ToolCall> calls) => new()
    {
        Role = AssistantRole.Assistant,
        Content = text,
        ToolCalls = calls,
        Timestamp = DateTime.UtcNow
    };

    /// <summary>
    /// Creates a tool result message to be sent back to the provider after executing a tool call.
    /// </summary>
    public static AssistantMessage Tool(string toolCallId, string resultContent, bool isError = false) => new()
    {
        Role = AssistantRole.Tool,
        Content = isError ? $"[Erro: {resultContent}]" : resultContent,
        ToolCallId = toolCallId,
        Timestamp = DateTime.UtcNow
    };
}
