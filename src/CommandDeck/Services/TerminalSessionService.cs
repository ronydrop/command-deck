using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Implements <see cref="ITerminalSessionService"/>.
/// Sits above <see cref="ITerminalService"/> and adds extended state tracking,
/// per-session command history, output snapshots, and multi-view attach/detach.
/// </summary>
public sealed partial class TerminalSessionService : ITerminalSessionService
{
    private readonly ITerminalService _terminalService;
    private readonly ConcurrentDictionary<string, TerminalSessionModel> _models = new();

    // Per-session output listener subscriptions (multiple ViewModels can listen)
    private readonly ConcurrentDictionary<string, ConcurrentBag<Action<string>>> _outputListeners = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<Action<SessionState>>> _stateListeners = new();

    // Idle/busy detection
    private readonly ConcurrentDictionary<string, DateTime> _lastOutputTimestamp = new();
    private readonly ConcurrentDictionary<string, Timer> _idleDetectionTimers = new();

    // Regex to detect shell prompts (common patterns)
    private static readonly Regex[] PromptPatterns =
    {
        new(@"\x1b\[\d*[mGKH]*[^\x1b]*[#$%>\]]\s*$", RegexOptions.Compiled),  // colored prompt
        new(@"\$\s*$", RegexOptions.Compiled),   // $ prompt
        new(@"#\s*$", RegexOptions.Compiled),   // # prompt (root)
        new(@">\s*$", RegexOptions.Compiled),   // > prompt (PS, CMD)
        new(@"PS>\s*$", RegexOptions.Compiled),  // PowerShell prompt
        new(@">>\s*$", RegexOptions.Compiled),  // continuation prompt
    };

    // Regex to detect commands (a line that looks like user input before Enter)
    private static readonly Regex CommandPattern = new(
        @"^(?:\x1b\[[\d;]*[A-Za-z])*[^\r\n]+$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private const int IdleDetectionDelayMs = 1000; // ms after last output before declaring idle

    public event Action<TerminalSessionModel>? SessionCreated;
    public event Action<string>? SessionClosed;
    public event Action<string, SessionState>? SessionStateChanged;

    public TerminalSessionService(ITerminalService terminalService)
    {
        _terminalService = terminalService;

        // Subscribe to low-level events
        _terminalService.OutputReceived += OnTerminalOutputReceived;
        _terminalService.SessionExited += OnTerminalSessionExited;
        _terminalService.TitleChanged += OnTerminalTitleChanged;
    }

    // ─── Lifecycle ──────────────────────────────────────────────────────────

    public async Task<TerminalSessionModel> CreateSessionAsync(
        ShellType shellType,
        string? workingDirectory = null,
        string? projectId = null,
        short columns = 120,
        short rows = 30)
    {
        var runtimeSession = await _terminalService.CreateSessionAsync(
            shellType, workingDirectory, projectId, columns, rows);

        var model = new TerminalSessionModel
        {
            Id = runtimeSession.Id,
            Title = runtimeSession.Title,
            ShellType = runtimeSession.ShellType,
            ProjectId = runtimeSession.ProjectId,
            WorkingDirectory = runtimeSession.WorkingDirectory,
            SessionState = runtimeSession.Status == TerminalStatus.Running
                ? SessionState.Idle
                : SessionState.Error,
            CreatedAt = runtimeSession.StartedAt,
            ErrorCode = runtimeSession.Status == TerminalStatus.Error ? -1 : 0,
        };

        _models[model.Id] = model;
        _lastOutputTimestamp[model.Id] = DateTime.UtcNow;
        _outputListeners[model.Id] = new ConcurrentBag<Action<string>>();
        _stateListeners[model.Id] = new ConcurrentBag<Action<SessionState>>();

        // Start idle detection timer
        StartIdleDetection(model.Id);

        SessionCreated?.Invoke(model);
        return model;
    }

    public async Task CloseSessionAsync(string sessionId)
    {
        StopIdleDetection(sessionId);

        // Notify listeners of state change before closing
        SetSessionState(sessionId, SessionState.Stopped);

        try
        {
            await _terminalService.CloseSessionAsync(sessionId);
        }
        catch
        {
            // Best-effort close
        }

        // Remove all listeners
        _outputListeners.TryRemove(sessionId, out _);
        _stateListeners.TryRemove(sessionId, out _);
        _lastOutputTimestamp.TryRemove(sessionId, out _);

        if (_models.TryGetValue(sessionId, out var model))
        {
            model.MarkClosed();
        }

        SessionClosed?.Invoke(sessionId);
    }

    public async Task CloseAllSessionsAsync()
    {
        var sessionIds = _models.Keys.ToList();
        foreach (var id in sessionIds)
        {
            await CloseSessionAsync(id);
        }
    }

    // ─── Attach / Detach ────────────────────────────────────────────────────

    public IDisposable AttachOutputListener(string sessionId, Action<string> callback)
    {
        if (!_outputListeners.TryGetValue(sessionId, out var listeners))
        {
            listeners = new ConcurrentBag<Action<string>>();
            _outputListeners[sessionId] = listeners;
        }

        listeners.Add(callback);

        return new Subscription(() =>
        {
            // Note: ConcurrentBag doesn't have Remove, so we use a filter-based approach.
            // We replace the bag contents filtering out our callback.
            if (_outputListeners.TryGetValue(sessionId, out var current))
            {
                var filtered = new ConcurrentBag<Action<string>>();
                foreach (var cb in current)
                {
                    if (cb != callback)
                        filtered.Add(cb);
                }
                _outputListeners[sessionId] = filtered;
            }
        });
    }

    public IDisposable AttachStateListener(string sessionId, Action<SessionState> callback)
    {
        if (!_stateListeners.TryGetValue(sessionId, out var listeners))
        {
            listeners = new ConcurrentBag<Action<SessionState>>();
            _stateListeners[sessionId] = listeners;
        }

        listeners.Add(callback);

        return new Subscription(() =>
        {
            if (_stateListeners.TryGetValue(sessionId, out var current))
            {
                var filtered = new ConcurrentBag<Action<SessionState>>();
                foreach (var cb in current)
                {
                    if (cb != callback)
                        filtered.Add(cb);
                }
                _stateListeners[sessionId] = filtered;
            }
        });
    }

    // ─── Input ──────────────────────────────────────────────────────────────

    public async Task WriteAsync(string sessionId, string text)
    {
        if (!_models.TryGetValue(sessionId, out var model))
            return;

        // Detect if this is a command submission (ends with newline/CR)
        bool isCommand = text.EndsWith('\n') || text.EndsWith('\r');

        if (isCommand)
        {
            // Extract the command text (strip trailing newlines)
            var command = text.TrimEnd('\r', '\n');
            model.RecordCommand(command);
            SetSessionState(sessionId, SessionState.Busy);
        }
        else
        {
            // Partial input (user typing) - still counts as activity
            model.UpdateLastActivity();
        }

        await _terminalService.WriteAsync(sessionId, text);
    }

    public async Task SendKeyAsync(string sessionId, string keySequence)
    {
        if (!_models.ContainsKey(sessionId))
            return;

        // Raw keys: treat Tab as potential "waiting input" signal
        // Treat Arrow keys as navigation (activity only)
        if (_models.TryGetValue(sessionId, out var model))
        {
            model.UpdateLastActivity();
        }

        await _terminalService.SendKeyAsync(sessionId, keySequence);
    }

    public void Resize(string sessionId, short columns, short rows)
    {
        _terminalService.Resize(sessionId, columns, rows);
    }

    // ─── Queries ────────────────────────────────────────────────────────────

    public TerminalSessionModel? GetSession(string sessionId) =>
        _models.TryGetValue(sessionId, out var model) ? model : null;

    public IReadOnlyList<TerminalSessionModel> GetAllSessions() =>
        _models.Values.ToList().AsReadOnly();

    public IReadOnlyList<TerminalSessionModel> GetActiveSessions() =>
        _models.Values
            .Where(m => m.SessionState is SessionState.Idle or SessionState.Busy or SessionState.WaitingInput or SessionState.Starting)
            .ToList()
            .AsReadOnly();

    // ─── Search ─────────────────────────────────────────────────────────────

    public IReadOnlyList<(string SessionId, string Command)> SearchCommandHistory(
        string query,
        string? sessionId = null,
        int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var results = new List<(string SessionId, string Command)>();
        var queryLower = query.ToLowerInvariant();

        foreach (var model in _models.Values)
        {
            if (sessionId != null && model.Id != sessionId)
                continue;

            foreach (var cmd in model.CommandHistory.GetAll())
            {
                if (cmd.ToLowerInvariant().Contains(queryLower))
                {
                    results.Add((model.Id, cmd));
                    if (results.Count >= maxResults)
                        return results;
                }
            }
        }

        return results;
    }

    public IReadOnlyList<(string SessionId, int MatchCount)> SearchOutput(
        string query,
        string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var results = new List<(string SessionId, int MatchCount)>();

        foreach (var model in _models.Values)
        {
            if (sessionId != null && model.Id != sessionId)
                continue;

            var snapshot = model.OutputSnapshot;
            var count = CountOccurrences(snapshot, query);
            if (count > 0)
            {
                results.Add((model.Id, count));
            }
        }

        return results.OrderByDescending(r => r.MatchCount).ToList();
    }

    // ─── Private: Event Handlers ────────────────────────────────────────────

    private void OnTerminalOutputReceived(string sessionId, string output)
    {
        if (!_models.TryGetValue(sessionId, out var model))
            return;

        // 1. Update output snapshot for search
        model.AppendOutput(output);

        // 2. Update last output timestamp for idle detection
        _lastOutputTimestamp[sessionId] = DateTime.UtcNow;

        // 3. If currently idle and output detected, mark as busy
        if (model.SessionState == SessionState.Idle)
        {
            // Check if output looks like a prompt (then it's still idle)
            if (!LooksLikePrompt(output))
            {
                SetSessionState(sessionId, SessionState.Busy);
            }
        }

        // 4. Forward to all attached listeners
        if (_outputListeners.TryGetValue(sessionId, out var listeners))
        {
            foreach (var callback in listeners)
            {
                try
                {
                    callback(output);
                }
                catch
                {
                    // Listener fault should not affect other listeners or the service
                }
            }
        }
    }

    private void OnTerminalSessionExited(string sessionId)
    {
        StopIdleDetection(sessionId);

        if (_models.TryGetValue(sessionId, out var model))
        {
            model.MarkClosed();
        }

        SetSessionState(sessionId, SessionState.Stopped);
        SessionClosed?.Invoke(sessionId);
    }

    private void OnTerminalTitleChanged(string sessionId, string newTitle)
    {
        if (_models.TryGetValue(sessionId, out var model))
        {
            model.Title = newTitle;
        }
    }

    // ─── Private: State Management ──────────────────────────────────────────

    private void SetSessionState(string sessionId, SessionState newState)
    {
        if (!_models.TryGetValue(sessionId, out var model))
            return;

        var oldState = model.SessionState;
        if (oldState == newState)
            return;

        model.SessionState = newState;
        SessionStateChanged?.Invoke(sessionId, newState);

        // Notify state listeners
        if (_stateListeners.TryGetValue(sessionId, out var listeners))
        {
            foreach (var callback in listeners)
            {
                try
                {
                    callback(newState);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TerminalSessionService] State-change listener threw: {ex.Message}");
                }
            }
        }
    }

    private void StartIdleDetection(string sessionId)
    {
        var timer = new Timer(
            _ => CheckIdleState(sessionId),
            null,
            IdleDetectionDelayMs,
            IdleDetectionDelayMs);

        _idleDetectionTimers[sessionId] = timer;
    }

    private void StopIdleDetection(string sessionId)
    {
        if (_idleDetectionTimers.TryRemove(sessionId, out var timer))
        {
            timer.Dispose();
        }
    }

    private void CheckIdleState(string sessionId)
    {
        if (!_models.TryGetValue(sessionId, out var model))
            return;

        if (model.SessionState != SessionState.Busy)
            return;

        if (!_lastOutputTimestamp.TryGetValue(sessionId, out var lastOutput))
            return;

        // If no output for the detection delay period, consider the session idle again
        if ((DateTime.UtcNow - lastOutput).TotalMilliseconds >= IdleDetectionDelayMs)
        {
            SetSessionState(sessionId, SessionState.Idle);
        }
    }

    /// <summary>
    /// Quick heuristic: does the output look like a shell prompt?
    /// </summary>
    private static bool LooksLikePrompt(string output)
    {
        // Strip trailing whitespace for matching
        var trimmed = output.TrimEnd();

        foreach (var pattern in PromptPatterns)
        {
            if (pattern.IsMatch(trimmed))
                return true;
        }

        // Very short output that's just whitespace or a prompt char
        if (trimmed.Length <= 4 && (trimmed.EndsWith('$') || trimmed.EndsWith('>') ||
            trimmed.EndsWith('#') || trimmed.EndsWith(']')))
        {
            return true;
        }

        return false;
    }

    // ─── Private: Utilities ─────────────────────────────────────────────────

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
            return 0;

        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    // ─── IDisposable ────────────────────────────────────────────────────────

    public void Dispose()
    {
        _terminalService.OutputReceived -= OnTerminalOutputReceived;
        _terminalService.SessionExited -= OnTerminalSessionExited;
        _terminalService.TitleChanged -= OnTerminalTitleChanged;

        foreach (var timer in _idleDetectionTimers.Values)
        {
            timer.Dispose();
        }
        _idleDetectionTimers.Clear();

        GC.SuppressFinalize(this);
    }

    // ─── Nested: Subscription Token ─────────────────────────────────────────

    /// <summary>
    /// Simple disposable that invokes an action on dispose.
    /// Used as the token returned by Attach* methods.
    /// </summary>
    private sealed class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private int _disposed;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _unsubscribe();
            }
        }
    }
}
