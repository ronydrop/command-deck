namespace CommandDeck.Models;

/// <summary>
/// Represents a single stash entry from <c>git stash list</c>.
/// </summary>
public class GitStashEntry
{
    /// <summary>Stash ref (e.g. "stash@{0}").</summary>
    public string Index { get; set; } = string.Empty;

    /// <summary>Human-readable stash message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Relative time string (e.g. "3 hours ago").</summary>
    public string RelativeTime { get; set; } = string.Empty;
}
