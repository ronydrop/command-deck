using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandDeck.Models;

/// <summary>
/// Represents a single message in the AI assistant chat history.
/// </summary>
public partial class ChatMessage : ObservableObject
{
    /// <summary>"user" or "assistant".</summary>
    public string Role { get; init; } = "user";

    /// <summary>Text content of the message.</summary>
    [ObservableProperty]
    private string _content = string.Empty;

    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>True when the role is "user".</summary>
    public bool IsUser => Role == "user";

    /// <summary>True while the message is still being streamed or generated.</summary>
    [ObservableProperty]
    private bool _isStreaming;

    // ─── Factory helpers ──────────────────────────────────────────────────────

    public static ChatMessage FromUser(string content) =>
        new() { Role = "user", Content = content };

    public static ChatMessage FromAssistant(string content) =>
        new() { Role = "assistant", Content = content };

    public static ChatMessage Loading() =>
        new() { Role = "assistant", Content = string.Empty, IsStreaming = true };
}
