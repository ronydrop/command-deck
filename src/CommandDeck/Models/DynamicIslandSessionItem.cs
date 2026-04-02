using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandDeck.Models;

/// <summary>
/// Lightweight presentation model for a terminal session displayed in the Dynamic Island widget.
/// </summary>
public partial class DynamicIslandSessionItem : ObservableObject
{
    // Immutable identity — set once at construction, never change
    public string SessionId { get; init; } = string.Empty;
    public ShellType ShellType { get; init; } = ShellType.WSL;
    public bool IsAiSession { get; init; }
    public string AiModelUsed { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Mutable — may update after creation
    [ObservableProperty]
    private string _title = "Terminal";

    [ObservableProperty]
    private SessionState _sessionState = SessionState.Idle;

    /// <summary>Human-readable duration string updated by DynamicIslandViewModel timer.</summary>
    [ObservableProperty]
    private string _durationDisplay = "0s";

    public void UpdateDuration()
    {
        var elapsed = DateTime.UtcNow - CreatedAt;
        DurationDisplay = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
            : elapsed.TotalMinutes >= 1
                ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
                : $"{elapsed.Seconds}s";
    }
}
