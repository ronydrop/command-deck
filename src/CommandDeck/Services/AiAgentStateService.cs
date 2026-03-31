using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CommandDeck.Helpers;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Analyses stripped terminal output to detect AI agent semantic states.
/// Only processes sessions explicitly registered via <see cref="RegisterSession"/>.
/// </summary>
public sealed class AiAgentStateService : IAiAgentStateService, IDisposable
{
    private const int MaxBufferLength = 2048;
    private const int TailWindowLength = 500;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CompletedToIdleDelay = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, SessionBuffer> _sessions = new();
    private readonly Dispatcher _dispatcher;

    public event Action<AiAgentStateChangedArgs>? StateChanged;

    // ─── Detection patterns (compiled, priority order) ──────────────────────

    private static readonly Regex WaitingUserPattern = new(
        @"\[Y/n\]|Do you want to proceed|Allow|press Enter|Approve|Deny|approve this|deny this",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ErrorPattern = new(
        @"(?:^|\n)\s*(?:Error:|ERROR|✗|FAILED|error\[|panic:)",
        RegexOptions.Compiled);

    private static readonly Regex ExecutingPattern = new(
        @"(?:Read|Edit|Write|Bash|Glob|Grep|Agent|MultiEdit|NotebookEdit|WebSearch|WebFetch|TodoWrite|TaskCreate)\s*[(\[]",
        RegexOptions.Compiled);

    private static readonly Regex ThinkingPattern = new(
        @"Thinking|thinking\.\.\.|⠋|⠙|⠹|⠸|⠼|⠴|⠦|⠧|⠇|⠏|\.{3,}$",
        RegexOptions.Compiled);

    private static readonly Regex ShellPromptPattern = new(
        @"[\$❯>]\s*$",
        RegexOptions.Compiled);

    public AiAgentStateService(ITerminalService terminalService)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        terminalService.OutputReceived += OnTerminalOutput;
        terminalService.SessionExited += OnTerminalExited;
    }

    public void RegisterSession(string sessionId)
    {
        _sessions.TryAdd(sessionId, new SessionBuffer(_dispatcher));
    }

    public void UnregisterSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var buf))
            buf.Dispose();
    }

    public AiAgentState GetState(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var buf)
            ? buf.CurrentState
            : AiAgentState.Idle;
    }

    private void OnTerminalOutput(string sessionId, string rawOutput)
    {
        if (!_sessions.TryGetValue(sessionId, out var buf))
            return;

        var plain = AnsiTextHelper.StripAnsi(rawOutput);
        buf.Append(plain);

        var tail = buf.GetTail(TailWindowLength);
        var detected = DetectState(tail);

        // WaitingUser fires immediately — user needs to act
        if (detected == AiAgentState.WaitingUser)
        {
            buf.CancelDebounce();
            TransitionTo(sessionId, buf, detected);
            return;
        }

        // Debounce all other state transitions
        buf.ScheduleDebounce(DebounceDelay, () =>
        {
            // Re-read tail after debounce
            var freshTail = buf.GetTail(TailWindowLength);
            var freshState = DetectState(freshTail);
            TransitionTo(sessionId, buf, freshState);
        });

        // Reset idle timer — if output stops, transition to Completed/Idle
        buf.ResetIdleTimer(IdleTimeout, () =>
        {
            var nextState = buf.CurrentState is AiAgentState.Thinking or AiAgentState.Executing
                ? AiAgentState.Completed
                : AiAgentState.Idle;
            TransitionTo(sessionId, buf, nextState);

            if (nextState == AiAgentState.Completed)
            {
                buf.ResetIdleTimer(CompletedToIdleDelay, () =>
                    TransitionTo(sessionId, buf, AiAgentState.Idle));
            }
        });
    }

    private void OnTerminalExited(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var buf))
            return;

        _dispatcher.BeginInvoke(() => TransitionTo(sessionId, buf, AiAgentState.Completed));
    }

    private static AiAgentState DetectState(string tail)
    {
        // Priority order: WaitingUser > Error > Executing > Thinking > ShellPrompt
        if (WaitingUserPattern.IsMatch(tail))
            return AiAgentState.WaitingUser;

        if (ErrorPattern.IsMatch(tail))
            return AiAgentState.Error;

        if (ExecutingPattern.IsMatch(tail))
            return AiAgentState.Executing;

        if (ThinkingPattern.IsMatch(tail))
            return AiAgentState.Thinking;

        if (ShellPromptPattern.IsMatch(tail))
            return AiAgentState.Idle;

        return AiAgentState.Thinking; // default during output flow
    }

    private void TransitionTo(string sessionId, SessionBuffer buf, AiAgentState newState)
    {
        if (buf.CurrentState == newState)
            return;

        buf.CurrentState = newState;
        var args = new AiAgentStateChangedArgs
        {
            SessionId = sessionId,
            State = newState,
            Icon = AiAgentStateChangedArgs.GetIcon(newState),
            Label = AiAgentStateChangedArgs.GetLabel(newState)
        };

        _dispatcher.BeginInvoke(() => StateChanged?.Invoke(args));
    }

    public void Dispose()
    {
        foreach (var buf in _sessions.Values)
            buf.Dispose();
        _sessions.Clear();
    }

    // ─── Per-session rolling buffer ─────────────────────────────────────────

    private sealed class SessionBuffer : IDisposable
    {
        private readonly StringBuilder _buffer = new(MaxBufferLength);
        private readonly object _lock = new();
        private readonly Dispatcher _dispatcher;
        private DispatcherTimer? _debounceTimer;
        private EventHandler? _debounceHandler;
        private DispatcherTimer? _idleTimer;
        private EventHandler? _idleHandler;

        public AiAgentState CurrentState { get; set; } = AiAgentState.Idle;

        public SessionBuffer(Dispatcher dispatcher) => _dispatcher = dispatcher;

        public void Append(string text)
        {
            lock (_lock)
            {
                _buffer.Append(text);
                if (_buffer.Length > MaxBufferLength)
                {
                    int excess = _buffer.Length - MaxBufferLength;
                    _buffer.Remove(0, excess);
                }
            }
        }

        public string GetTail(int length)
        {
            lock (_lock)
            {
                if (_buffer.Length <= length)
                    return _buffer.ToString();
                return _buffer.ToString(_buffer.Length - length, length);
            }
        }

        public void ScheduleDebounce(TimeSpan delay, Action callback)
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (_debounceTimer is not null)
                {
                    _debounceTimer.Stop();
                    _debounceTimer.Tick -= _debounceHandler;
                }
                else
                {
                    _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
                }

                _debounceTimer.Interval = delay;
                _debounceHandler = (_, _) =>
                {
                    _debounceTimer.Stop();
                    callback();
                };
                _debounceTimer.Tick += _debounceHandler;
                _debounceTimer.Start();
            });
        }

        public void CancelDebounce()
        {
            _dispatcher.BeginInvoke(() => _debounceTimer?.Stop());
        }

        public void ResetIdleTimer(TimeSpan delay, Action callback)
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (_idleTimer is not null)
                {
                    _idleTimer.Stop();
                    _idleTimer.Tick -= _idleHandler;
                }
                else
                {
                    _idleTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
                }

                _idleTimer.Interval = delay;
                _idleHandler = (_, _) =>
                {
                    _idleTimer.Stop();
                    callback();
                };
                _idleTimer.Tick += _idleHandler;
                _idleTimer.Start();
            });
        }

        public void Dispose()
        {
            _debounceTimer?.Stop();
            _idleTimer?.Stop();
        }
    }
}
