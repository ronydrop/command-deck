using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Manages the lifecycle of terminal session models. Orchestrates the lower-level
/// <see cref="ITerminalService"/> and adds: extended state tracking (idle/busy/waiting),
/// per-session command history, output snapshot for search, and attach/detach semantics
/// for multiple ViewModels sharing the same session data.
/// </summary>
public interface ITerminalSessionService : IDisposable
{
    // ─── Lifecycle ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new terminal session via <see cref="ITerminalService"/> and wraps
    /// it in a <see cref="TerminalSessionModel"/> with full state tracking.
    /// </summary>
    /// <returns>The newly created session model.</returns>
    Task<TerminalSessionModel> CreateSessionAsync(
        ShellType shellType,
        string? workingDirectory = null,
        string? projectId = null,
        short columns = 120,
        short rows = 30);

    /// <summary>
    /// Closes a terminal session and marks its model as closed.
    /// Detaches all ViewModels automatically.
    /// </summary>
    Task CloseSessionAsync(string sessionId);

    /// <summary>
    /// Closes all active sessions.
    /// </summary>
    Task CloseAllSessionsAsync();

    // ─── Attach / Detach (multi-view support) ──────────────────────────────

    /// <summary>
    /// Attaches a consumer to receive output events for a session.
    /// Used by <see cref="ViewModels.TerminalViewModel"/> and other listeners.
    /// Returns false if the session does not exist.
    /// </summary>
    /// <param name="sessionId">Session to attach to.</param>
    /// <param name="callback">Called with plain-text output as it arrives.</param>
    /// <returns>A disposable token. Call Dispose to detach.</returns>
    IDisposable AttachOutputListener(string sessionId, Action<string> callback);

    /// <summary>
    /// Subscribes to state changes for a specific session.
    /// </summary>
    IDisposable AttachStateListener(string sessionId, Action<SessionState> callback);

    // ─── Input ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes input text to a session and records it as a command (if it ends with newline/CR).
    /// </summary>
    Task WriteAsync(string sessionId, string text);

    /// <summary>
    /// Sends raw key data to a session (no command recording).
    /// </summary>
    Task SendKeyAsync(string sessionId, string keySequence);

    /// <summary>
    /// Resizes a session.
    /// </summary>
    void Resize(string sessionId, short columns, short rows);

    // ─── Queries ────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the session model by ID, or null if not found.
    /// </summary>
    TerminalSessionModel? GetSession(string sessionId);

    /// <summary>
    /// Gets all tracked session models (including closed ones still in memory).
    /// </summary>
    IReadOnlyList<TerminalSessionModel> GetAllSessions();

    /// <summary>
    /// Gets only sessions that are not closed/stopped/error.
    /// </summary>
    IReadOnlyList<TerminalSessionModel> GetActiveSessions();

    // ─── History ────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches command history across all sessions (or a specific one).
    /// </summary>
    IReadOnlyList<(string SessionId, string Command)> SearchCommandHistory(
        string query,
        string? sessionId = null,
        int maxResults = 20);

    /// <summary>
    /// Searches output snapshots across all sessions (or a specific one).
    /// </summary>
    IReadOnlyList<(string SessionId, int MatchCount)> SearchOutput(
        string query,
        string? sessionId = null);

    // ─── Events ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when a new session is created.
    /// </summary>
    event Action<TerminalSessionModel>? SessionCreated;

    /// <summary>
    /// Raised when a session is closed.
    /// </summary>
    event Action<string>? SessionClosed;

    /// <summary>
    /// Raised when a session's state changes (Idle -> Busy, etc.).
    /// </summary>
    event Action<string, SessionState>? SessionStateChanged;

    /// <summary>
    /// Raised when a session's title changes (via OSC terminal escape sequence).
    /// </summary>
    event Action<string, string>? SessionTitleChanged;
}
