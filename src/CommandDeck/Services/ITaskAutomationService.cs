namespace CommandDeck.Services;

/// <summary>
/// Orchestrates the automated dispatch of a Kanban card to an AI agent:
/// dependency check → brief generation → terminal launch → card state management.
/// </summary>
public interface ITaskAutomationService
{
    /// <summary>
    /// Verifies that all <c>CardRefs</c> are in the <em>done</em> column.
    /// </summary>
    Task<bool> CanLaunchCardAsync(string boardId, string cardId, CancellationToken ct = default);

    /// <summary>
    /// Checks dependencies, writes the brief, launches the agent in a new terminal,
    /// and moves the card to <em>running</em>.
    /// Returns the created terminal session ID.
    /// Throws <see cref="InvalidOperationException"/> if dependencies are unmet.
    /// </summary>
    Task<string> LaunchCardAsync(
        string boardId, string cardId,
        string? workingDirectory = null,
        CancellationToken ct = default);
}
