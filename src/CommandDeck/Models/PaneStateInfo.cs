using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandDeck.Models;

/// <summary>
/// State of a terminal pane, mirroring Zellij's claude-pane-notify states.
/// </summary>
public enum PaneState
{
    /// <summary>Terminal is idle, no command running.</summary>
    Idle,
    /// <summary>A command is currently executing.</summary>
    Running,
    /// <summary>Terminal is waiting for user input.</summary>
    Waiting,
    /// <summary>Last command has finished.</summary>
    Done
}

/// <summary>
/// Tracks the visual state of a single terminal pane, including icon,
/// custom title, and scroll animation offset.
/// </summary>
public partial class PaneStateInfo : ObservableObject
{
    /// <summary>
    /// The terminal/canvas-item ID this state belongs to.
    /// </summary>
    public string PaneId { get; init; } = string.Empty;

    /// <summary>
    /// Current pane state.
    /// </summary>
    [ObservableProperty]
    private PaneState _state = PaneState.Idle;

    /// <summary>
    /// Icon representing the current state.
    /// </summary>
    [ObservableProperty]
    private string _icon = string.Empty;

    /// <summary>
    /// Optional custom title (e.g. "npm install..."). Null when idle.
    /// </summary>
    [ObservableProperty]
    private string? _title;

    /// <summary>
    /// Timestamp of the last state change.
    /// </summary>
    [ObservableProperty]
    private DateTime _lastUpdated = DateTime.Now;

    /// <summary>
    /// Current scroll offset for long title animation.
    /// </summary>
    [ObservableProperty]
    private int _scrollOffset;

    /// <summary>
    /// Returns the icon string for a given PaneState.
    /// </summary>
    public static string GetIconForState(PaneState state) => state switch
    {
        PaneState.Running => "\u26A1",  // ⚡
        PaneState.Waiting => "\uD83D\uDD14", // 🔔
        PaneState.Done    => "\u2705",  // ✅
        _                 => string.Empty
    };

    /// <summary>
    /// Maximum display width for scrolling titles (in characters).
    /// </summary>
    public const int MaxDisplayWidth = 28;

    /// <summary>
    /// Gets the visible portion of the title with scroll applied.
    /// </summary>
    public string GetScrolledTitle()
    {
        if (string.IsNullOrEmpty(Title) || Title.Length <= MaxDisplayWidth)
            return Title ?? string.Empty;

        // Circular scroll like Zellij
        var padded = Title + "   " + Title;
        var offset = ScrollOffset % (Title.Length + 3);
        return padded.Substring(offset, Math.Min(MaxDisplayWidth, padded.Length - offset));
    }
}
