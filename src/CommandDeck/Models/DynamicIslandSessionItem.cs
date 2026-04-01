using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandDeck.Models;

/// <summary>
/// Lightweight presentation model for a terminal session displayed in the Dynamic Island widget.
/// </summary>
public partial class DynamicIslandSessionItem : ObservableObject
{
    [ObservableProperty]
    private string _sessionId = string.Empty;

    [ObservableProperty]
    private string _title = "Terminal";

    [ObservableProperty]
    private ShellType _shellType = ShellType.WSL;

    [ObservableProperty]
    private SessionState _sessionState = SessionState.Idle;

    [ObservableProperty]
    private bool _isAiSession;

    [ObservableProperty]
    private string _aiModelUsed = string.Empty;

    [ObservableProperty]
    private DateTime _createdAt = DateTime.UtcNow;

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
