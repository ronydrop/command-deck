namespace CommandDeck.Models;

/// <summary>
/// Database record for a persisted chat message.
/// </summary>
public class ChatMessageRecord
{
    public long Id { get; init; }
    public string ConversationId { get; init; } = string.Empty;
    public string Role { get; init; } = "user";
    public string Content { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public string? Model { get; init; }
    public string? Provider { get; init; }
}
