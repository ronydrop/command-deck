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

    /// <summary>True when this assistant message contains an error.</summary>
    public bool HasError => !IsUser && !IsStreaming && (
        Content.Contains("[Erro:", StringComparison.OrdinalIgnoreCase) ||
        Content.StartsWith("Erro", StringComparison.OrdinalIgnoreCase) ||
        Content.StartsWith("AI Error:", StringComparison.OrdinalIgnoreCase));

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnIsStreamingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    // ─── Factory helpers ──────────────────────────────────────────────────────

    public static ChatMessage FromUser(string content) =>
        new() { Role = "user", Content = content };

    public static ChatMessage FromAssistant(string content) =>
        new() { Role = "assistant", Content = content };

    public static ChatMessage Loading() =>
        new() { Role = "assistant", Content = string.Empty, IsStreaming = true };
}
