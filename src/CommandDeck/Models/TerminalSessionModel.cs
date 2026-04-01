using CommunityToolkit.Mvvm.ComponentModel;
using CommandDeck.Helpers;

namespace CommandDeck.Models;

/// <summary>
/// Extended state of a terminal session beyond the runtime TerminalStatus.
/// Tracks whether the shell is idle, busy executing, or waiting for input.
/// </summary>
public enum SessionState
{
    /// <summary>Session is starting up.</summary>
    Starting,

    /// <summary>Shell is idle at prompt, waiting for user input.</summary>
    Idle,

    /// <summary>Shell is executing a command.</summary>
    Busy,

    /// <summary>Shell is waiting for user input (e.g., confirmation prompt, password).</summary>
    WaitingInput,

    /// <summary>Session has been stopped.</summary>
    Stopped,

    /// <summary>Session encountered an error.</summary>
    Error
}

/// <summary>
/// Rich model for terminal session persistence and state tracking.
/// Separated from the runtime <see cref="TerminalSession"/> which holds ConPTY handles.
/// This model is suitable for SQLite persistence and search indexing.
/// </summary>
public partial class TerminalSessionModel : ObservableObject
{
    // ─── Core Identity ─────────────────────────────────────────────────────

    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _title = "Terminal";

    [ObservableProperty]
    private ShellType _shellType = ShellType.WSL;

    [ObservableProperty]
    private string? _projectId;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    // ─── AI Session Metadata ─────────────────────────────────────────────────

    [ObservableProperty]
    private AiSessionType _aiSessionType = AiSessionType.None;

    [ObservableProperty]
    private string _aiModelUsed = string.Empty;

    public bool IsAiSession => AiSessionType != AiSessionType.None;

    // ─── Extended State ─────────────────────────────────────────────────────

    [ObservableProperty]
    private SessionState _sessionState = SessionState.Starting;

    [ObservableProperty]
    private DateTime _lastActivityTimestamp = DateTime.UtcNow;

    [ObservableProperty]
    private DateTime _createdAt = DateTime.UtcNow;

    [ObservableProperty]
    private DateTime? _closedAt;

    [ObservableProperty]
    private int _errorCode;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    // ─── Command History (ring buffer, thread-safe) ─────────────────────────

    /// <summary>Ring buffer that stores per-session command history with deduplication.</summary>
    public CommandRingBuffer CommandHistory { get; } = new(capacity: 500);

    /// <summary>
    /// Total number of commands executed in this session (includes trimmed ones).
    /// </summary>
    public int CommandCount => CommandHistory.TotalCount;

    // ─── Output Snapshot (plain text for search) ────────────────────────────

    /// <summary>Plain-text output snapshot buffer for search/indexing.</summary>
    public TerminalOutputBuffer OutputBuffer { get; } = new();

    /// <summary>
    /// Plain-text snapshot of terminal output for search/indexing.
    /// ANSI sequences are stripped. Automatically trimmed to 64 KB.
    /// </summary>
    public string OutputSnapshot => OutputBuffer.GetContent();

    // ─── Methods ────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a command in the session's per-session history ring buffer.
    /// </summary>
    /// <param name="command">The raw command text (trimmed).</param>
    public void RecordCommand(string command)
    {
        CommandHistory.Add(command);
        UpdateLastActivity();
    }

    /// <summary>
    /// Appends plain-text output to the snapshot buffer.
    /// Strips basic ANSI escape sequences to keep text searchable.
    /// Automatically trims when exceeding 64 KB.
    /// </summary>
    /// <param name="output">Raw output text (may contain ANSI sequences).</param>
    public void AppendOutput(string output)
    {
        OutputBuffer.Append(output);
        UpdateLastActivity();
    }

    /// <summary>
    /// Touches the last-activity timestamp to the current UTC time.
    /// </summary>
    public void UpdateLastActivity()
    {
        LastActivityTimestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the session as closed with an optional error.
    /// </summary>
    public void MarkClosed(int errorCode = 0, string? errorMessage = null)
    {
        SessionState = errorCode == 0 ? SessionState.Stopped : SessionState.Error;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage ?? string.Empty;
        ClosedAt = DateTime.UtcNow;
        UpdateLastActivity();
    }

    /// <summary>
    /// Navigates command history backward (for up-arrow functionality).
    /// Returns the command at the given index relative to current position.
    /// </summary>
    /// <param name="currentIndex">0-based from the most recent command. 0 = latest.</param>
    /// <returns>The command text, or null if index is out of range.</returns>
    public string? GetCommandAt(int index) => CommandHistory.GetAt(index);

    /// <summary>
    /// Clears all runtime data (history, output snapshot) while preserving identity fields.
    /// Useful for re-initializing a persisted session.
    /// </summary>
    public void ClearRuntimeData()
    {
        CommandHistory.Clear();
        OutputBuffer.Clear();
    }

}
