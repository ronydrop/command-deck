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

    /// <summary>
    /// True when the message bubble should be visible.
    /// Stays visible as soon as any text arrives, even while still streaming.
    /// </summary>
    public bool ShowContent => Content.Length > 0 || !IsStreaming;

    /// <summary>
    /// True when the animated dots indicator should be visible (loading, no text yet).
    /// </summary>
    public bool ShowDots => IsStreaming && Content.Length == 0;

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowDots));
    }

    partial void OnIsStreamingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowDots));
    }

    // ─── Factory helpers ──────────────────────────────────────────────────────

    public static ChatMessage FromUser(string content) =>
        new() { Role = "user", Content = content };

    public static ChatMessage FromAssistant(string content) =>
        new() { Role = "assistant", Content = content };

    public static ChatMessage Loading() =>
        new() { Role = "assistant", Content = string.Empty, IsStreaming = true };

    /// <summary>
    /// Creates a system info message (slash command output, tool feedback, etc.).
    /// Displayed differently from user/assistant messages.
    /// </summary>
    public static ChatMessage FromSystem(string content) =>
        new() { Role = "system", Content = content };

    /// <summary>
    /// Creates a tool-invocation chip shown in the chat while a tool is executing or after it completes.
    /// </summary>
    public static ChatMessage ToolInvocation(string toolName, string inputJson, string resultContent) =>
        new()
        {
            Role = "tool",
            Content = $"**{toolName}**\n{resultContent}",
        };
}
