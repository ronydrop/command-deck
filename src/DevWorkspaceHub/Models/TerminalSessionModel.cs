using System.Collections.Concurrent;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using DevWorkspaceHub.Helpers;

namespace DevWorkspaceHub.Models;

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
    /// <summary>
    /// Maximum number of commands kept in the per-session ring buffer.
    /// </summary>
    private const int MaxCommandHistory = 500;

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

    private readonly ConcurrentQueue<string> _commandQueue = new();
    private int _commandCount;

    /// <summary>
    /// Total number of commands executed in this session (includes trimmed ones).
    /// </summary>
    public int CommandCount => _commandCount;

    /// <summary>
    /// Current commands in the ring buffer (up to 500 most recent).
    /// </summary>
    public IReadOnlyList<string> CommandHistory
    {
        get
        {
            // Snapshot: the queue may be modified concurrently, but ConcurrentQueue
            // enumeration is safe and represents a point-in-time view.
            return _commandQueue.ToArray();
        }
    }

    // ─── Output Snapshot (plain text for search) ────────────────────────────

    private const int MaxOutputSnapshotLength = 64 * 1024; // 64 KB max

    private readonly StringBuilder _outputBuilder = new(16 * 1024);
    private readonly object _outputLock = new();

    /// <summary>
    /// Plain-text snapshot of terminal output for search/indexing.
    /// ANSI sequences are stripped. Automatically trimmed to 64 KB.
    /// </summary>
    public string OutputSnapshot
    {
        get
        {
            lock (_outputLock)
            {
                return _outputBuilder.ToString();
            }
        }
    }

    // ─── Methods ────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a command in the session's per-session history ring buffer.
    /// </summary>
    /// <param name="command">The raw command text (trimmed).</param>
    public void RecordCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        var trimmed = command.Trim();

        // Deduplicate: if the last recorded command is identical, skip it.
        if (_commandQueue.TryPeek(out var last) && last == trimmed)
            return;

        _commandQueue.Enqueue(trimmed);

        // Trim oldest if over capacity
        while (_commandQueue.Count > MaxCommandHistory)
        {
            _commandQueue.TryDequeue(out _);
        }

        Interlocked.Increment(ref _commandCount);
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
        if (string.IsNullOrEmpty(output))
            return;

        // Strip ANSI escape sequences for plain-text search
        var plain = AnsiTextHelper.StripAnsi(output);

        lock (_outputLock)
        {
            _outputBuilder.Append(plain);

            // Trim from the beginning if we exceed the limit
            if (_outputBuilder.Length > MaxOutputSnapshotLength)
            {
                var excess = _outputBuilder.Length - MaxOutputSnapshotLength;
                _outputBuilder.Remove(0, excess);
            }
        }

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
    public string? GetCommandAt(int index)
    {
        var commands = _commandQueue.ToArray();
        // commands[0] is oldest, commands[^1] is newest
        // index 0 should return newest
        var reverseIndex = commands.Length - 1 - index;
        if (reverseIndex < 0 || reverseIndex >= commands.Length)
            return null;
        return commands[reverseIndex];
    }

    /// <summary>
    /// Clears all runtime data (history, output snapshot) while preserving identity fields.
    /// Useful for re-initializing a persisted session.
    /// </summary>
    public void ClearRuntimeData()
    {
        while (_commandQueue.TryDequeue(out _)) { }
        _commandCount = 0;

        lock (_outputLock)
        {
            _outputBuilder.Clear();
        }
    }

}
