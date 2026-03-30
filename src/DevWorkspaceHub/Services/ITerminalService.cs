using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Service for managing terminal sessions using ConPTY.
/// </summary>
public interface ITerminalService
{
    /// <summary>
    /// Creates a new terminal session with the specified shell.
    /// </summary>
    Task<TerminalSession> CreateSessionAsync(
        ShellType shellType,
        string? workingDirectory = null,
        string? projectId = null,
        short columns = 120,
        short rows = 30);

    /// <summary>
    /// Writes input text to a terminal session.
    /// </summary>
    Task WriteAsync(string sessionId, string text);

    /// <summary>
    /// Sends a keypress (including special keys) to a terminal session.
    /// </summary>
    Task SendKeyAsync(string sessionId, string keySequence);

    /// <summary>
    /// Resizes a terminal session.
    /// </summary>
    void Resize(string sessionId, short columns, short rows);

    /// <summary>
    /// Closes and disposes a terminal session.
    /// </summary>
    Task CloseSessionAsync(string sessionId);

    /// <summary>
    /// Closes all active sessions.
    /// </summary>
    Task CloseAllSessionsAsync();

    /// <summary>
    /// Gets all active terminal sessions.
    /// </summary>
    IReadOnlyList<TerminalSession> GetSessions();

    /// <summary>
    /// Gets a specific terminal session by ID.
    /// </summary>
    TerminalSession? GetSession(string sessionId);

    /// <summary>
    /// Event raised when terminal output is received.
    /// </summary>
    event Action<string, string>? OutputReceived;

    /// <summary>
    /// Event raised when a terminal session exits.
    /// </summary>
    event Action<string>? SessionExited;

    /// <summary>
    /// Event raised when the terminal title changes (via OSC sequence).
    /// </summary>
    event Action<string, string>? TitleChanged;
}
