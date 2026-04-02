using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the Dynamic Island floating overlay window.
/// Surfaces prioritized events (AI + notifications) and a secondary session list.
/// </summary>
public partial class DynamicIslandViewModel : ObservableObject, IDisposable
{
    private readonly ITerminalSessionService _sessionService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IDynamicIslandEventService _eventService;
    private readonly Lazy<MainViewModel> _mainViewModel;
    private DispatcherTimer? _durationTimer;
    private bool _disposed;
    private bool _savingVisibility;
    private DynamicIslandEventItem? _primaryEventSubscription;

    public ObservableCollection<DynamicIslandSessionItem> Sessions { get; } = new();

    /// <summary>Prioritized event feed (AI + promoted notifications).</summary>
    public ReadOnlyObservableCollection<DynamicIslandEventItem> IslandEvents => _eventService.Events;

    /// <summary>Events after the current primary (secondary queue, capped for display).</summary>
    public ObservableCollection<DynamicIslandEventItem> QueuedEvents { get; } = new();

    [ObservableProperty]
    private bool _isVisible = false; // start hidden; InitializeAsync sets the persisted value

    [ObservableProperty]
    private int _activeSessionCount;

    [ObservableProperty]
    private bool _hasBusySession;

    [ObservableProperty]
    private DynamicIslandState _islandState = DynamicIslandState.Pill;

    /// <summary>Top-priority island event, or null when the feed is empty.</summary>
    [ObservableProperty]
    private DynamicIslandEventItem? _primaryEvent;

    /// <summary>Agent label from <see cref="PrimaryEvent"/> for compact bindings.</summary>
    [ObservableProperty]
    private string _currentAgentLabel = string.Empty;

    /// <summary>One-line preview from <see cref="PrimaryEvent"/>.</summary>
    [ObservableProperty]
    private string _currentPreview = string.Empty;

    /// <summary>Tool/error secondary line for the pill when <see cref="DynamicIslandEventItem.ActionDetail"/> is set.</summary>
    [ObservableProperty]
    private string _currentActionDetail = string.Empty;

    /// <summary>When the queue has more than the configured visible limit, how many are hidden.</summary>
    [ObservableProperty]
    private int _queuedOverflowCount;

    /// <summary>Localized hint e.g. "e mais 3" when <see cref="QueuedOverflowCount"/> &gt; 0.</summary>
    [ObservableProperty]
    private string _queuedOverflowHint = string.Empty;

    /// <summary>True when there are events after the primary item.</summary>
    [ObservableProperty]
    private bool _hasQueuedEvents;

    /// <summary>True when the primary AI event is in a busy visual state (thinking / executing).</summary>
    [ObservableProperty]
    private bool _isPrimaryAgentBusy;

    [ObservableProperty]
    private int _attentionEventCount;

    [ObservableProperty]
    private int _busyEventCount;

    [ObservableProperty]
    private int _decisionEventCount;

    /// <summary>
    /// Fired when the island should slide down from minimized state.
    /// The code-behind subscribes to run the slide-down animation.
    /// </summary>
    public event Action? RequestShowFromMinimized;

    public DynamicIslandViewModel(
        ITerminalSessionService sessionService,
        ISettingsService settingsService,
        INotificationService notificationService,
        IDynamicIslandEventService eventService,
        Lazy<MainViewModel> mainViewModel)
    {
        _sessionService = sessionService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _eventService = eventService;
        _mainViewModel = mainViewModel;

        _sessionService.SessionCreated += OnSessionCreated;
        _sessionService.SessionClosed += OnSessionClosed;
        _sessionService.SessionStateChanged += OnSessionStateChanged;
        _sessionService.SessionTitleChanged += OnSessionTitleChanged;
        _notificationService.NotificationAdded += OnNotificationAdded;

        _eventService.PrimaryEventChanged += OnPrimaryIslandEventChanged;
        if (_eventService.Events is INotifyCollectionChanged incc)
            incc.CollectionChanged += OnIslandEventsCollectionChanged;

        _settingsService.SettingsChanged += OnAppSettingsChanged;

        SyncPrimaryFromService();
    }

    private void OnAppSettingsChanged(AppSettings _)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            RefreshQueuedEvents();
        });
    }

    /// <summary>
    /// Loads existing active sessions. Called from App.InitializeServicesAsync after DI is ready.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            IsVisible = settings.IsDynamicIslandEnabled;

            var active = _sessionService.GetActiveSessions();
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var session in active)
                    AddSessionItem(session);

                RefreshCounts();

                // Start timer on the UI thread so it captures the correct Dispatcher
                _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _durationTimer.Tick += (_, _) => UpdateDurations();
                _durationTimer.Start();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIsland] InitializeAsync failed: {ex}");
            IsVisible = true; // fallback: show the island if settings can't be loaded
        }
    }

    [RelayCommand]
    private void NavigateToSession(string sessionId)
    {
        try
        {
            _mainViewModel.Value.FocusSessionById(sessionId);
            Application.Current.MainWindow?.Activate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIsland] NavigateToSession failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task CloseSession(string sessionId)
    {
        try
        {
            await _sessionService.CloseSessionAsync(sessionId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIsland] CloseSession failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task ToggleVisibility()
    {
        // Guard against rapid double-invocation causing a race on the settings file
        if (_savingVisibility) return;

        IsVisible = !IsVisible;
        _savingVisibility = true;
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.IsDynamicIslandEnabled = IsVisible;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIsland] ToggleVisibility save failed: {ex}");
        }
        finally
        {
            _savingVisibility = false;
        }
    }

    [RelayCommand]
    private void Minimize()
    {
        // The code-behind handles the slide-up animation via IslandState PropertyChanged
        IslandState = DynamicIslandState.Minimized;
    }

    private void OnSessionCreated(TerminalSessionModel model)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (Sessions.Any(s => s.SessionId == model.Id)) return;
            AddSessionItem(model);
            RefreshCounts();
            TriggerShowIfMinimized();
        });
    }

    private void OnSessionTitleChanged(string sessionId, string newTitle)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var item = Sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (item != null)
                item.Title = newTitle;
        });
    }

    private void OnSessionClosed(string sessionId)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var item = Sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (item != null)
                Sessions.Remove(item);

            RefreshCounts();
        });
    }

    private void OnSessionStateChanged(string sessionId, SessionState newState)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var item = Sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (item != null)
                item.SessionState = newState;

            RefreshCounts();
        });
    }

    private void OnNotificationAdded(NotificationItem item)
    {
        Application.Current.Dispatcher.BeginInvoke(() => TriggerShowIfMinimized());
    }

    private void OnPrimaryIslandEventChanged(DynamicIslandEventItem? _)
    {
        Application.Current.Dispatcher.BeginInvoke(SyncPrimaryFromService);
    }

    private void OnIslandEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(SyncPrimaryFromService);
    }

    private void SyncPrimaryFromService()
    {
        UnsubscribePrimaryEvent();

        PrimaryEvent = _eventService.PrimaryEvent;
        CurrentAgentLabel = PrimaryEvent?.AgentLabel ?? string.Empty;
        CurrentPreview = PrimaryEvent?.PrimarySnippet ?? PrimaryEvent?.PreviewText ?? string.Empty;
        CurrentActionDetail = PrimaryEvent?.SecondarySnippet ?? PrimaryEvent?.ActionDetail ?? string.Empty;
        IsPrimaryAgentBusy = PrimaryEvent?.AgentState is AiAgentState.Thinking or AiAgentState.Executing;

        SubscribePrimaryEvent(PrimaryEvent);
        RefreshQueuedEvents();
        RefreshEventCounters();
    }

    private void SubscribePrimaryEvent(DynamicIslandEventItem? evt)
    {
        if (evt == null) return;
        _primaryEventSubscription = evt;
        evt.PropertyChanged += OnPrimaryEventPropertyChanged;
    }

    private void UnsubscribePrimaryEvent()
    {
        if (_primaryEventSubscription is null) return;
        _primaryEventSubscription.PropertyChanged -= OnPrimaryEventPropertyChanged;
        _primaryEventSubscription = null;
    }

    private void OnPrimaryEventPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DynamicIslandEventItem evt) return;
        if (e.PropertyName is nameof(DynamicIslandEventItem.PreviewText)
            or nameof(DynamicIslandEventItem.PrimarySnippet)
            or nameof(DynamicIslandEventItem.SecondarySnippet)
            or nameof(DynamicIslandEventItem.Title)
            or nameof(DynamicIslandEventItem.AgentLabel)
            or nameof(DynamicIslandEventItem.AgentState)
            or nameof(DynamicIslandEventItem.ActionDetail))
        {
            CurrentAgentLabel = evt.AgentLabel;
            CurrentPreview = string.IsNullOrWhiteSpace(evt.PrimarySnippet) ? evt.PreviewText : evt.PrimarySnippet;
            CurrentActionDetail = string.IsNullOrWhiteSpace(evt.SecondarySnippet) ? evt.ActionDetail ?? string.Empty : evt.SecondarySnippet;
            IsPrimaryAgentBusy = evt.AgentState is AiAgentState.Thinking or AiAgentState.Executing;
        }
    }

    private void RefreshQueuedEvents()
    {
        var maxVisible = Math.Clamp(_settingsService.CurrentSettings.DynamicIslandQueueVisibleLimit, 1, 20);
        QueuedEvents.Clear();
        var list = _eventService.Events;
        var queue = new List<DynamicIslandEventItem>();
        for (var i = 1; i < list.Count; i++)
            queue.Add(list[i]);

        var total = queue.Count;
        var visible = Math.Min(maxVisible, total);
        for (var i = 0; i < visible; i++)
            QueuedEvents.Add(queue[i]);

        QueuedOverflowCount = Math.Max(0, total - maxVisible);
        QueuedOverflowHint = QueuedOverflowCount > 0 ? $"e mais {QueuedOverflowCount}" : string.Empty;
        HasQueuedEvents = total > 0;
        RefreshEventCounters();
    }

    private void RefreshEventCounters()
    {
        var list = _eventService.Events;
        AttentionEventCount = list.Count(e => e.EventKind is DynamicIslandEventKind.Approval or DynamicIslandEventKind.Question or DynamicIslandEventKind.Error);
        BusyEventCount = list.Count(e => e.AgentState is AiAgentState.Thinking or AiAgentState.Executing);
        DecisionEventCount = list.Count(e => e.IsDecisionEvent);
    }

    private void TriggerShowIfMinimized()
    {
        if (IslandState != DynamicIslandState.Minimized) return;
        IslandState = DynamicIslandState.Pill;
        RequestShowFromMinimized?.Invoke();
    }

    private void AddSessionItem(TerminalSessionModel model)
    {
        var item = new DynamicIslandSessionItem
        {
            SessionId = model.Id,
            Title = model.Title,
            ShellType = model.ShellType,
            SessionState = model.SessionState,
            IsAiSession = model.IsAiSession,
            AiModelUsed = model.AiModelUsed,
            CreatedAt = model.CreatedAt
        };
        item.UpdateDuration();
        Sessions.Add(item);
    }

    private void RefreshCounts()
    {
        ActiveSessionCount = Sessions.Count;
        HasBusySession = Sessions.Any(s => s.SessionState == SessionState.Busy);
    }

    private void UpdateDurations()
    {
        foreach (var item in Sessions)
            item.UpdateDuration();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribePrimaryEvent();
        _eventService.PrimaryEventChanged -= OnPrimaryIslandEventChanged;
        if (_eventService.Events is INotifyCollectionChanged incc)
            incc.CollectionChanged -= OnIslandEventsCollectionChanged;

        _sessionService.SessionCreated -= OnSessionCreated;
        _sessionService.SessionClosed -= OnSessionClosed;
        _sessionService.SessionStateChanged -= OnSessionStateChanged;
        _sessionService.SessionTitleChanged -= OnSessionTitleChanged;
        _notificationService.NotificationAdded -= OnNotificationAdded;
        _settingsService.SettingsChanged -= OnAppSettingsChanged;

        _durationTimer?.Stop();
        GC.SuppressFinalize(this);
    }
}
