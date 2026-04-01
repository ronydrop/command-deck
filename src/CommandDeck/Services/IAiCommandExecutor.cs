namespace CommandDeck.Services;

/// <summary>
/// Orchestrates AI commands that require terminal context:
/// fixing the last error and forwarding output to an AI session.
/// </summary>
public interface IAiCommandExecutor
{
    /// <summary>Builds a fix-error prompt and dispatches it to the active AI terminal or assistant panel.</summary>
    /// <param name="sessionId">Source terminal session id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task FixLastErrorAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Builds a send-context prompt from the active terminal output and forwards it to an AI session.</summary>
    /// <param name="sessionId">Source terminal session id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendOutputToAiAsync(string sessionId, CancellationToken ct = default);
}
