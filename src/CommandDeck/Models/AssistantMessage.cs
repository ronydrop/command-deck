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
    Assistant
}

/// <summary>
/// A single message in the assistant conversation thread.
/// Maps to the OpenAI chat completion message format.
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
    /// Creates a system message for instructions.
    /// </summary>
    public static AssistantMessage System(string content) => new()
    {
        Role = AssistantRole.System,
        Content = content,
        Timestamp = DateTime.UtcNow
    };

    /// <summary>
    /// Creates a user message.
    /// </summary>
    public static AssistantMessage User(string content) => new()
    {
        Role = AssistantRole.User,
        Content = content,
        Timestamp = DateTime.UtcNow
    };

    /// <summary>
    /// Creates an assistant response message.
    /// </summary>
    public static AssistantMessage Assistant(string content) => new()
    {
        Role = AssistantRole.Assistant,
        Content = content,
        Timestamp = DateTime.UtcNow
    };
}
