using System.Collections.Concurrent;
using System.Windows.Threading;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Tracks terminal pane states (Idle/Running/Waiting/Done), manages
/// scrolling title animations and icon aggregation for the status bar.
/// Mirrors the logic of Zellij's claude-pane-notify plugin.
/// </summary>
public class PaneStateService : IPaneStateService, IDisposable
{
    private readonly ConcurrentDictionary<string, PaneStateInfo> _paneStates = new();
    private readonly ConcurrentDictionary<string, DispatcherTimer> _idleTimers = new();
    private readonly DispatcherTimer _scrollTimer;
    private readonly Dispatcher _dispatcher;
    private readonly INotificationService _notificationService;

    /// <summary>
    /// After output stops flowing, wait this long before transitioning to Idle.
    /// Prevents flicker between Running→Idle on rapid output bursts.
    /// </summary>
    private static readonly TimeSpan IdleDebounceDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// After Done state, auto-transition to Idle after this delay.
    /// </summary>
    private static readonly TimeSpan DoneToIdleDelay = TimeSpan.FromSeconds(5);

    public event Action<PaneStateInfo>? StateChanged;
    public event Action<string>? AggregatedIconsChanged;

    public PaneStateService(INotificationService notificationService, ITerminalService terminalService)
    {
        _notificationService = notificationService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // Scroll timer at 400ms — same interval as Zellij
        _scrollTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _scrollTimer.Tick += OnScrollTick;

        // Subscribe to terminal events
        terminalService.OutputReceived += OnTerminalOutput;
        terminalService.SessionExited += OnTerminalExited;
    }

    /// <summary>
    /// When terminal output is received, mark pane as Running and reset idle debounce.
    /// </summary>
    private void OnTerminalOutput(string sessionId, string output)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var info = _paneStates.GetOrAdd(sessionId, id => new PaneStateInfo { PaneId = id });

            // Only transition to Running if not already Running (avoid flooding StateChanged)
            if (info.State != PaneState.Running)
            {
                SetState(sessionId, PaneState.Running);
            }

            // Reset the idle debounce timer: after output stops for IdleDebounceDelay, go Idle
            ResetIdleTimer(sessionId, IdleDebounceDelay);
        });
    }

    /// <summary>
    /// When a terminal session exits, mark pane as Done.
    /// It will auto-transition to Idle after DoneToIdleDelay.
    /// </summary>
    private void OnTerminalExited(string sessionId)
    {
        _dispatcher.BeginInvoke(() =>
        {
            SetState(sessionId, PaneState.Done);
            ResetIdleTimer(sessionId, DoneToIdleDelay);
        });
    }

    /// <summary>
    /// Resets or creates a debounce timer that will transition pane to Idle.
    /// </summary>
    private void ResetIdleTimer(string paneId, TimeSpan delay)
    {
        // Cancel existing timer
        if (_idleTimers.TryRemove(paneId, out var existing))
            existing.Stop();

        var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = delay
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _idleTimers.TryRemove(paneId, out _);
            ResetToIdle(paneId);
        };
        _idleTimers[paneId] = timer;
        timer.Start();
    }

    public void SetState(string paneId, PaneState state, string? title = null)
    {
        _dispatcher.Invoke(() =>
        {
            var info = _paneStates.GetOrAdd(paneId, id => new PaneStateInfo { PaneId = id });
            var previousState = info.State;

            info.State = state;
            info.Icon = PaneStateInfo.GetIconForState(state);
            info.LastUpdated = DateTime.Now;
            info.ScrollOffset = 0;

            if (state == PaneState.Idle)
            {
                info.Title = null;
            }
            else if (title is not null)
            {
                info.Title = title;
            }

            // Emit notification on meaningful transitions
            EmitTransitionNotification(paneId, previousState, state, title);

            StateChanged?.Invoke(info);
            AggregatedIconsChanged?.Invoke(GetAggregatedIcons());

            // Start/stop scroll timer based on whether any pane has a long title
            UpdateScrollTimer();
        });
    }

    public PaneStateInfo? GetState(string paneId)
    {
        _paneStates.TryGetValue(paneId, out var info);
        return info;
    }

    public void ResetToIdle(string paneId)
    {
        SetState(paneId, PaneState.Idle);
    }

    public void RemovePane(string paneId)
    {
        _dispatcher.Invoke(() =>
        {
            _paneStates.TryRemove(paneId, out _);
            if (_idleTimers.TryRemove(paneId, out var timer))
                timer.Stop();
            AggregatedIconsChanged?.Invoke(GetAggregatedIcons());
            UpdateScrollTimer();
        });
    }

    public string GetAggregatedIcons()
    {
        var icons = _paneStates.Values
            .Where(p => p.State != PaneState.Idle)
            .OrderBy(p => p.LastUpdated)
            .Select(p => p.Icon)
            .Where(i => !string.IsNullOrEmpty(i));

        return string.Join(" ", icons);
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        foreach (var info in _paneStates.Values)
        {
            if (info.State != PaneState.Idle && info.Title is { Length: > PaneStateInfo.MaxDisplayWidth })
            {
                info.ScrollOffset++;
            }
        }
    }

    private void UpdateScrollTimer()
    {
        var needsScroll = _paneStates.Values.Any(p =>
            p.State != PaneState.Idle &&
            p.Title is { Length: > PaneStateInfo.MaxDisplayWidth });

        if (needsScroll && !_scrollTimer.IsEnabled)
            _scrollTimer.Start();
        else if (!needsScroll && _scrollTimer.IsEnabled)
            _scrollTimer.Stop();
    }

    private void EmitTransitionNotification(string paneId, PaneState from, PaneState to, string? title)
    {
        // Only emit on meaningful transitions, not repeated same-state updates
        if (from == to) return;

        switch (to)
        {
            case PaneState.Done:
                _notificationService.Notify(
                    title: title ?? "Comando finalizado",
                    type: NotificationType.Success,
                    source: NotificationSource.Terminal,
                    relatedItemId: paneId);
                break;

            case PaneState.Waiting when from == PaneState.Running:
                _notificationService.Notify(
                    title: title ?? "Terminal aguardando input",
                    type: NotificationType.Info,
                    source: NotificationSource.Terminal,
                    relatedItemId: paneId);
                break;
        }
    }

    public void Dispose()
    {
        _scrollTimer.Stop();
        foreach (var timer in _idleTimers.Values)
            timer.Stop();
        _idleTimers.Clear();
        _paneStates.Clear();
        GC.SuppressFinalize(this);
    }
}
