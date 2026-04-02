using System.Collections.ObjectModel;
using System.Media;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Helpers;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Services;

/// <summary>
/// Aggregates AI agent state changes and notifications into a prioritized
/// event feed for the Dynamic Island overlay.
/// </summary>
public sealed class DynamicIslandEventService : IDynamicIslandEventService, IDisposable
{
    private readonly IAiAgentStateService _aiStateService;
    private readonly INotificationService _notificationService;
    private readonly ITerminalSessionService _sessionService;
    private readonly ITerminalService _terminalService;
    private readonly ISettingsService _settingsService;
    private readonly Lazy<MainViewModel> _mainViewModel;
    private readonly ObservableCollection<DynamicIslandEventItem> _events = new();
    private readonly DispatcherTimer _expiryTimer;
    private bool _disposed;

    // Track one event per AI session to dedup/coalesce
    private readonly Dictionary<string, DynamicIslandEventItem> _aiSessionEvents = new();

    /// <summary>Avoids repeating the same attention sound while the session stays in the same critical state.</summary>
    private readonly Dictionary<string, AiAgentState> _criticalSoundLastState = new();

    public ReadOnlyObservableCollection<DynamicIslandEventItem> Events { get; }

    private DynamicIslandEventItem? _primaryEvent;
    public DynamicIslandEventItem? PrimaryEvent
    {
        get => _primaryEvent;
        private set
        {
            if (_primaryEvent == value) return;
            _primaryEvent = value;
            PrimaryEventChanged?.Invoke(value);
        }
    }

    public event Action<DynamicIslandEventItem?>? PrimaryEventChanged;
    public event Action<DynamicIslandEventItem>? EventAdded;
    public event Action<DynamicIslandEventItem>? EventRemoved;

    public DynamicIslandEventService(
        IAiAgentStateService aiStateService,
        INotificationService notificationService,
        ITerminalSessionService sessionService,
        ITerminalService terminalService,
        ISettingsService settingsService,
        Lazy<MainViewModel> mainViewModel)
    {
        _aiStateService = aiStateService;
        _notificationService = notificationService;
        _sessionService = sessionService;
        _terminalService = terminalService;
        _settingsService = settingsService;
        _mainViewModel = mainViewModel;

        Events = new ReadOnlyObservableCollection<DynamicIslandEventItem>(_events);

        _aiStateService.StateChanged += OnAiStateChanged;
        _notificationService.NotificationAdded += OnNotificationAdded;
        _sessionService.SessionClosed += OnSessionClosed;

        _expiryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _expiryTimer.Tick += (_, _) => PurgeExpired();
        _expiryTimer.Start();
    }

    private void OnAiStateChanged(AiAgentStateChangedArgs args)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            // Idle = remove the AI event for this session
            if (args.State == AiAgentState.Idle)
            {
                _criticalSoundLastState.Remove(args.SessionId);
                if (_aiSessionEvents.TryGetValue(args.SessionId, out var existing))
                {
                    RemoveEvent(existing);
                    _aiSessionEvents.Remove(args.SessionId);
                }
                return;
            }

            // Reset attention-sound memory when leaving critical / error (so the next occurrence plays again).
            if (args.State is not (AiAgentState.WaitingUser or AiAgentState.WaitingInput or AiAgentState.Error))
                _criticalSoundLastState.Remove(args.SessionId);

            // Get session metadata for agent label
            var session = _sessionService.GetActiveSessions()
                .FirstOrDefault(s => s.Id == args.SessionId);
            var agentLabel = session?.AiSessionType switch
            {
                AiSessionType.Claude or AiSessionType.ClaudeResume => "Claude",
                AiSessionType.Codex => "Codex",
                AiSessionType.Aider => "Aider",
                AiSessionType.Gemini => "Gemini",
                AiSessionType.Copilot => "Copilot",
                _ => "AI"
            };

            var priority = args.State switch
            {
                AiAgentState.WaitingUser => IslandEventPriority.Critical,
                AiAgentState.WaitingInput => IslandEventPriority.Critical,
                AiAgentState.Error => IslandEventPriority.High,
                AiAgentState.Executing => IslandEventPriority.Normal,
                AiAgentState.Thinking => IslandEventPriority.Normal,
                AiAgentState.Completed => IslandEventPriority.Low,
                _ => IslandEventPriority.Low
            };

            var severity = args.State switch
            {
                AiAgentState.Error => NotificationType.Error,
                AiAgentState.WaitingUser or AiAgentState.WaitingInput => NotificationType.Warning,
                AiAgentState.Completed => NotificationType.Success,
                _ => NotificationType.Info
            };

            var title = DynamicIslandPresentationHelper.BuildHeadline(agentLabel, args.State, args.Label);
            var eventKind = DynamicIslandPresentationHelper.GetEventKind(args.State);
            var tone = DynamicIslandPresentationHelper.GetVisualTone(args.State, severity);
            var sessionTitle = args.SessionTitle ?? session?.Title ?? string.Empty;
            var primarySnippet = string.IsNullOrWhiteSpace(args.PrimarySnippet)
                ? DynamicIslandPresentationHelper.BuildPrimarySnippet(args.State, args.Label, args.ActionDetail, args.ChoiceOptions)
                : args.PrimarySnippet!;
            var secondarySnippet = string.IsNullOrWhiteSpace(args.SecondarySnippet)
                ? DynamicIslandPresentationHelper.BuildSecondarySnippet(args.State, args.Label, args.ActionDetail, args.ChoiceOptions)
                : args.SecondarySnippet!;

            // Completed events auto-expire after 8 seconds
            DateTime? expiresAt = args.State == AiAgentState.Completed
                ? DateTime.UtcNow.AddSeconds(8)
                : null;

            // Coalesce: update existing event for the same session instead of creating a new one
            if (_aiSessionEvents.TryGetValue(args.SessionId, out var evt))
            {
                evt.Title = title;
                evt.Subtitle = args.Label;
                evt.Icon = args.Icon;
                evt.AgentLabel = agentLabel;
                evt.AgentState = args.State;
                evt.Priority = priority;
                evt.Severity = severity;
                evt.PreviewText = args.Label;
                evt.ActionDetail = args.ActionDetail ?? string.Empty;
                evt.EventKind = eventKind;
                evt.AccentTone = tone;
                evt.PrimarySnippet = primarySnippet;
                evt.SecondarySnippet = secondarySnippet;
                evt.SessionTitle = sessionTitle;
                evt.CompactBadge = DynamicIslandPresentationHelper.BuildCompactBadge(eventKind);
                evt.SupportsMarkdown = args.SupportsMarkdown;
                evt.CanJumpToExactContext = args.CanJumpToExactContext;
                ReplaceChoiceOptions(evt, args.ChoiceOptions);
                ApplyActionCommands(evt);
                MaybePlayIslandSound(args.SessionId, args.State);
                ReorderAndRefreshPrimary();
                return;
            }

            // New event
            var newEvent = new DynamicIslandEventItem
            {
                SessionId = args.SessionId,
                Title = title,
                Subtitle = args.Label,
                PreviewText = args.Label,
                ActionDetail = args.ActionDetail ?? string.Empty,
                Icon = args.Icon,
                AgentLabel = agentLabel,
                AgentState = args.State,
                Priority = priority,
                Severity = severity,
                ExpiresAt = expiresAt,
                EventKind = eventKind,
                AccentTone = tone,
                PrimarySnippet = primarySnippet,
                SecondarySnippet = secondarySnippet,
                SessionTitle = sessionTitle,
                CompactBadge = DynamicIslandPresentationHelper.BuildCompactBadge(eventKind),
                SupportsMarkdown = args.SupportsMarkdown,
                CanJumpToExactContext = args.CanJumpToExactContext
            };

            ReplaceChoiceOptions(newEvent, args.ChoiceOptions);
            ApplyActionCommands(newEvent);
            _aiSessionEvents[args.SessionId] = newEvent;
            InsertEvent(newEvent);
            MaybePlayIslandSound(args.SessionId, args.State);
        });
    }

    private void ReplaceChoiceOptions(DynamicIslandEventItem evt, IReadOnlyList<AiAgentChoiceOption> choices)
    {
        evt.ChoiceOptions.Clear();
        foreach (var c in choices)
        {
            var sid = evt.SessionId;
            var send = c.SendText;
            evt.ChoiceOptions.Add(new DynamicIslandChoiceOption(c.Label,
                new AsyncRelayCommand(async () => await _terminalService.WriteAsync(sid, send))));
        }
    }

    private void MaybePlayIslandSound(string sessionId, AiAgentState state)
    {
        if (!_settingsService.CurrentSettings.DynamicIslandSoundEnabled) return;
        if (state is not (AiAgentState.WaitingUser or AiAgentState.WaitingInput or AiAgentState.Error)) return;
        if (_criticalSoundLastState.TryGetValue(sessionId, out var prev) && prev == state) return;
        _criticalSoundLastState[sessionId] = state;
        try
        {
            switch (state)
            {
                case AiAgentState.WaitingUser:
                    SystemSounds.Hand.Play();
                    break;
                case AiAgentState.WaitingInput:
                    SystemSounds.Question.Play();
                    break;
                case AiAgentState.Error:
                    SystemSounds.Exclamation.Play();
                    break;
            }
        }
        catch
        {
            // Ignore if system sound API unavailable.
        }
    }

    private void OnNotificationAdded(NotificationItem notification)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            // Only promote Warning/Error notifications to the island
            if (notification.Type is not (NotificationType.Warning or NotificationType.Error))
                return;

            var priority = notification.Type == NotificationType.Error
                ? IslandEventPriority.High
                : IslandEventPriority.Normal;

            var evt = new DynamicIslandEventItem
            {
                SessionId = notification.RelatedItemId ?? string.Empty,
                SourceNotificationId = notification.Id,
                Title = notification.Title,
                Subtitle = notification.Message ?? string.Empty,
                PreviewText = notification.Message ?? notification.Title,
                Icon = notification.Icon,
                Severity = notification.Type,
                Priority = priority,
                EventKind = DynamicIslandEventKind.Notification,
                AccentTone = DynamicIslandPresentationHelper.GetVisualTone(AiAgentState.Idle, notification.Type),
                PrimarySnippet = notification.Message ?? notification.Title,
                SecondarySnippet = "Clique para abrir a origem do alerta.",
                SessionTitle = ResolveSessionTitle(notification.RelatedItemId),
                CompactBadge = DynamicIslandPresentationHelper.BuildCompactBadge(DynamicIslandEventKind.Notification),
                CanJumpToExactContext = !string.IsNullOrWhiteSpace(notification.RelatedItemId),
                ExpiresAt = DateTime.UtcNow.AddSeconds(
                    notification.Type == NotificationType.Error ? 15 : 8)
            };

            ApplyActionCommands(evt);
            RemoveDuplicateNotifications(evt);
            InsertEvent(evt);
            PlayNotificationIslandSound(notification.Type);
        });
    }

    private void PlayNotificationIslandSound(NotificationType type)
    {
        if (!_settingsService.CurrentSettings.DynamicIslandSoundEnabled) return;
        try
        {
            if (type == NotificationType.Error)
                SystemSounds.Exclamation.Play();
            else
                SystemSounds.Asterisk.Play();
        }
        catch
        {
            // Ignore if system sound API unavailable.
        }
    }

    private bool IsAiTrackedEvent(DynamicIslandEventItem e) =>
        _aiSessionEvents.TryGetValue(e.SessionId, out var t) && t == e;

    /// <summary>
    /// Drops an older duplicate notification (same session + title + body + source id) so the feed does not spam.
    /// </summary>
    private void RemoveDuplicateNotifications(DynamicIslandEventItem incoming)
    {
        var windowSec = Math.Max(5, _settingsService.CurrentSettings.DynamicIslandNotificationDedupeWindowSeconds);
        var incomingId = incoming.SourceNotificationId ?? string.Empty;
        var stale = _events.Where(e => !IsAiTrackedEvent(e)
                                       && e.SessionId == incoming.SessionId
                                       && e.Title == incoming.Title
                                       && e.PreviewText == incoming.PreviewText
                                       && string.Equals(e.SourceNotificationId ?? string.Empty, incomingId, StringComparison.Ordinal)
                                       && (DateTime.UtcNow - e.CreatedAt).TotalSeconds <= windowSec)
            .ToList();
        if (stale.Count == 0) return;

        foreach (var e in stale)
        {
            _events.Remove(e);
            EventRemoved?.Invoke(e);
        }

        RefreshPrimary();
    }

    private void TrimFeedIfNeeded()
    {
        var maxFeed = Math.Clamp(_settingsService.CurrentSettings.DynamicIslandMaxFeedEvents, 5, 200);
        while (_events.Count > maxFeed)
        {
            var victim = _events[^1];
            if (_aiSessionEvents.TryGetValue(victim.SessionId, out var tracked) && tracked == victim)
                _aiSessionEvents.Remove(victim.SessionId);
            _events.RemoveAt(_events.Count - 1);
            EventRemoved?.Invoke(victim);
        }
    }

    private void OnSessionClosed(string sessionId)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_aiSessionEvents.TryGetValue(sessionId, out var evt))
            {
                RemoveEvent(evt);
                _aiSessionEvents.Remove(sessionId);
            }
            _criticalSoundLastState.Remove(sessionId);
        });
    }

    /// <summary>
    /// Wires Allow/Deny (terminal input), or focus-session actions for completed/error/notification events.
    /// </summary>
    private void ApplyActionCommands(DynamicIslandEventItem evt)
    {
        var sessionId = evt.SessionId;
        evt.PrimaryActionLabel = null;
        evt.SecondaryActionLabel = null;
        evt.PrimaryActionCommand = null;
        evt.SecondaryActionCommand = null;

        if (evt.AgentState != AiAgentState.WaitingInput)
            evt.ChoiceOptions.Clear();

        switch (evt.AgentState)
        {
            case AiAgentState.WaitingUser:
                evt.PrimaryActionLabel = "Permitir";
                evt.SecondaryActionLabel = "Negar";
                evt.PrimaryActionCommand = new AsyncRelayCommand(async () =>
                    await _terminalService.WriteAsync(sessionId, "y\r\n"));
                evt.SecondaryActionCommand = new AsyncRelayCommand(async () =>
                    await _terminalService.WriteAsync(sessionId, "n\r\n"));
                return;

            case AiAgentState.WaitingInput:
                if (evt.ChoiceOptions.Count > 0)
                {
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        evt.SecondaryActionLabel = "Abrir sessão";
                        evt.SecondaryActionCommand = new RelayCommand(() => FocusSession(sessionId));
                    }
                    return;
                }
                if (!string.IsNullOrEmpty(sessionId))
                {
                    evt.PrimaryActionLabel = "Abrir sessão";
                    evt.PrimaryActionCommand = new RelayCommand(() => FocusSession(sessionId));
                }
                return;

            case AiAgentState.Error:
            case AiAgentState.Completed:
                if (!string.IsNullOrEmpty(sessionId))
                {
                    evt.PrimaryActionLabel = "Abrir sessão";
                    evt.PrimaryActionCommand = new RelayCommand(() => FocusSession(sessionId));
                }
                return;

            case AiAgentState.Idle:
                // Notification-backed events (no AI semantic state) — optional focus when tied to a session
                if (!string.IsNullOrEmpty(sessionId))
                {
                    evt.PrimaryActionLabel = "Abrir sessão";
                    evt.PrimaryActionCommand = new RelayCommand(() => FocusSession(sessionId));
                }
                return;

            default:
                return;
        }
    }

    private void FocusSession(string sessionId)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            _mainViewModel.Value.FocusSessionById(sessionId)));
    }

    private string ResolveSessionTitle(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return string.Empty;

        return _sessionService.GetSession(sessionId)?.Title ?? string.Empty;
    }

    private void InsertEvent(DynamicIslandEventItem evt)
    {
        // Insert sorted by priority descending, then by creation time descending
        int index = 0;
        for (; index < _events.Count; index++)
        {
            if (evt.Priority > _events[index].Priority ||
                (evt.Priority == _events[index].Priority && evt.CreatedAt > _events[index].CreatedAt))
                break;
        }
        _events.Insert(index, evt);
        EventAdded?.Invoke(evt);
        TrimFeedIfNeeded();
        RefreshPrimary();
    }

    private void RemoveEvent(DynamicIslandEventItem evt)
    {
        _events.Remove(evt);
        EventRemoved?.Invoke(evt);
        RefreshPrimary();
    }

    private void ReorderAndRefreshPrimary()
    {
        // Re-sort in place (small collection, brute force is fine)
        var sorted = _events.OrderByDescending(e => e.Priority)
                            .ThenByDescending(e => e.CreatedAt)
                            .ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int oldIndex = _events.IndexOf(sorted[i]);
            if (oldIndex != i)
                _events.Move(oldIndex, i);
        }
        RefreshPrimary();
    }

    private void RefreshPrimary()
    {
        PrimaryEvent = _events.FirstOrDefault();
    }

    private void PurgeExpired()
    {
        var expired = _events.Where(e => e.IsExpired).ToList();
        foreach (var evt in expired)
        {
            _events.Remove(evt);
            if (_aiSessionEvents.TryGetValue(evt.SessionId, out var tracked) && tracked == evt)
                _aiSessionEvents.Remove(evt.SessionId);
            EventRemoved?.Invoke(evt);
        }
        if (expired.Count > 0)
            RefreshPrimary();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _expiryTimer.Stop();
        _aiStateService.StateChanged -= OnAiStateChanged;
        _notificationService.NotificationAdded -= OnNotificationAdded;
        _sessionService.SessionClosed -= OnSessionClosed;
    }
}
