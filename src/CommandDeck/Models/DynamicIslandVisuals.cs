namespace CommandDeck.Models;

/// <summary>
/// Semantic event kinds rendered by the Dynamic Island.
/// Allows the UI to switch between purpose-specific layouts.
/// </summary>
public enum DynamicIslandEventKind
{
    Activity = 0,
    Execution = 1,
    Approval = 2,
    Question = 3,
    Completed = 4,
    Error = 5,
    Notification = 6
}

/// <summary>
/// Visual tone used by the island to colorize cards, badges and animations.
/// </summary>
public enum DynamicIslandVisualTone
{
    Neutral = 0,
    Accent = 1,
    Success = 2,
    Warning = 3,
    Danger = 4
}
