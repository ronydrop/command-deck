using System.Collections.Concurrent;
using System.Linq;
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

    private static readonly Regex ToolNameRegex = new(
        @"\b((?:Read|Edit|Write|Bash|Glob|Grep|Agent|MultiEdit|NotebookEdit|WebSearch|WebFetch|TodoWrite|TaskCreate))\s*[(\[]",
        RegexOptions.Compiled);

    /// <summary>Captures tool name and first parenthesized argument for preview.</summary>
    private static readonly Regex ToolWithParenArgsRegex = new(
        @"\b((?:Read|Edit|Write|Bash|Glob|Grep|Agent|MultiEdit|NotebookEdit|WebSearch|WebFetch|TodoWrite|TaskCreate))\s*\(([^)]{0,400})\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        // WaitingUser / WaitingInput fire immediately — user needs to act
        if (detected is AiAgentState.WaitingUser or AiAgentState.WaitingInput)
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
        // Priority: WaitingUser > WaitingInput > Error > Executing > Thinking > ShellPrompt
        if (WaitingUserPattern.IsMatch(tail))
            return AiAgentState.WaitingUser;

        if (LooksLikeWaitingInput(tail))
            return AiAgentState.WaitingInput;

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

    private static bool LooksLikeWaitingInput(string tail)
    {
        if (string.IsNullOrWhiteSpace(tail)) return false;
        var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return false;

        int numbered = 0;
        foreach (var line in lines.TakeLast(14))
        {
            if (Regex.IsMatch(line, @"^\d+[\).\]]\s+\S"))
                numbered++;
        }
        if (numbered >= 2) return true;

        if (Regex.IsMatch(tail, @"(?i)(?:^|\n).*(?:choose|select|pick|type)\s+(?:one|a number|an option|option|the letter)"))
            return true;

        var last = lines[^1];
        if (last.Length < 120 && last.Contains('?') && !last.Contains("http", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private void TransitionTo(string sessionId, SessionBuffer buf, AiAgentState newState)
    {
        var tail = buf.GetTail(TailWindowLength);
        var displayLabel = BuildDisplayLabel(tail, newState);
        var choices = BuildChoiceOptions(tail, newState);
        var actionDetail = BuildActionDetail(tail, newState);
        var primarySnippet = DynamicIslandPresentationHelper.BuildPrimarySnippet(newState, displayLabel, actionDetail, choices);
        var secondarySnippet = DynamicIslandPresentationHelper.BuildSecondarySnippet(newState, displayLabel, actionDetail, choices);
        var supportsMarkdown = ContainsMarkdown(primarySnippet) || ContainsMarkdown(secondarySnippet);
        var sig = $"{newState}|{displayLabel}|{actionDetail ?? ""}|{primarySnippet}|{secondarySnippet}|{string.Join("|", choices.Select(c => $"{c.Label}\u001f{c.SendText}"))}";

        if (buf.CurrentState == newState && buf.LastSignature == sig)
            return;

        buf.CurrentState = newState;
        buf.LastSignature = sig;

        var args = new AiAgentStateChangedArgs
        {
            SessionId = sessionId,
            State = newState,
            Icon = AiAgentStateChangedArgs.GetIcon(newState),
            Label = displayLabel,
            ChoiceOptions = choices,
            ActionDetail = actionDetail,
            PrimarySnippet = primarySnippet,
            SecondarySnippet = secondarySnippet,
            SupportsMarkdown = supportsMarkdown,
            CanJumpToExactContext = true
        };

        _dispatcher.BeginInvoke(() => StateChanged?.Invoke(args));
    }

    private static string? BuildActionDetail(string tail, AiAgentState state)
    {
        return state switch
        {
            AiAgentState.Executing => ExtractLastToolArgumentSnippet(tail),
            AiAgentState.Error => ExtractErrorLineSnippet(tail),
            _ => null
        };
    }

    private static string? ExtractLastToolArgumentSnippet(string tail)
    {
        var matches = ToolWithParenArgsRegex.Matches(tail);
        if (matches.Count == 0) return null;
        var inner = matches[^1].Groups[2].Value;
        inner = Regex.Replace(inner, @"\s+", " ").Trim();
        if (string.IsNullOrEmpty(inner)) return null;
        if (inner.Length > 140)
            inner = inner[..137] + "...";
        return inner;
    }

    private static string? ExtractErrorLineSnippet(string tail)
    {
        var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (line.Length < 3) continue;
            if (line.Contains("Error:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                || line.Contains("FAILED", StringComparison.OrdinalIgnoreCase)
                || line.Contains("panic:", StringComparison.OrdinalIgnoreCase))
            {
                var s = line.Trim();
                return s.Length > 160 ? s[..157] + "..." : s;
            }
        }
        return null;
    }

    private static string BuildDisplayLabel(string tail, AiAgentState state)
    {
        return state switch
        {
            AiAgentState.Executing => ExtractLastToolName(tail) ?? AiAgentStateChangedArgs.GetLabel(state),
            AiAgentState.WaitingInput => ExtractQuestionSnippet(tail) ?? AiAgentStateChangedArgs.GetLabel(state),
            _ => AiAgentStateChangedArgs.GetLabel(state)
        };
    }

    private static string? ExtractLastToolName(string tail)
    {
        var matches = ToolNameRegex.Matches(tail);
        if (matches.Count == 0) return null;
        return matches[^1].Groups[1].Value + " (ferramenta)";
    }

    private static string? ExtractQuestionSnippet(string tail)
    {
        var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return null;
        foreach (var line in lines.TakeLast(8))
        {
            if (line.Contains('?') && line.Length < 200)
                return line.Trim();
        }
        return lines[^1].Length <= 160 ? lines[^1].Trim() : null;
    }

    private static IReadOnlyList<AiAgentChoiceOption> BuildChoiceOptions(string tail, AiAgentState state)
    {
        if (state != AiAgentState.WaitingInput) return Array.Empty<AiAgentChoiceOption>();

        var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<AiAgentChoiceOption>();
        var seen = new HashSet<string>();

        foreach (var line in lines.TakeLast(16))
        {
            var m = Regex.Match(line, @"^\s*(\d+)[\).\]]\s+(.+)$");
            if (m.Success)
            {
                var key = m.Groups[1].Value + "\u001f" + m.Groups[2].Value;
                if (seen.Add(key))
                    list.Add(new AiAgentChoiceOption(m.Groups[2].Value.Trim(), m.Groups[1].Value + "\r\n"));
                continue;
            }

            var m2 = Regex.Match(line, @"^\s*([A-Za-z])[\).\]]\s+(.+)$");
            if (m2.Success)
            {
                var key = m2.Groups[1].Value + "\u001f" + m2.Groups[2].Value;
                if (seen.Add(key))
                    list.Add(new AiAgentChoiceOption(m2.Groups[2].Value.Trim(), m2.Groups[1].Value + "\r\n"));
            }
        }

        return list.Count > 0 ? list.Take(8).ToList() : Array.Empty<AiAgentChoiceOption>();
    }

    private static bool ContainsMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("**", StringComparison.Ordinal)
               || text.Contains('`')
               || text.Contains("- ", StringComparison.Ordinal);
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

        /// <summary>Suppresses duplicate StateChanged when output updates but semantic state + label + choices are unchanged.</summary>
        public string LastSignature { get; set; } = string.Empty;

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
