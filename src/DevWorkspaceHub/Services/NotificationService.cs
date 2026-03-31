using System.Collections.ObjectModel;
using System.Windows.Threading;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Manages toast notifications and notification history.
/// Thread-safe: all collection mutations are dispatched to the UI thread.
/// </summary>
public class NotificationService : INotificationService
{
    private const int MaxActiveNotifications = 5;
    private const int MaxHistorySize = 50;

    private readonly ObservableCollection<NotificationItem> _history = new();
    private readonly Dictionary<string, DispatcherTimer> _dismissTimers = new();
    private readonly Dispatcher _dispatcher;
    private readonly ISettingsService _settingsService;

    public ObservableCollection<NotificationItem> ActiveNotifications { get; } = new();
    public ReadOnlyObservableCollection<NotificationItem> History { get; }
    public int UnreadCount => _history.Count(n => !n.IsRead);

    public event Action<NotificationItem>? NotificationAdded;
    public event Action<NotificationItem>? NotificationDismissed;

    public NotificationService(ISettingsService settingsService)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _settingsService = settingsService;
        History = new ReadOnlyObservableCollection<NotificationItem>(_history);
    }

    public void Notify(NotificationItem item)
    {
        // Check if notifications are enabled for this source
        if (!IsSourceEnabled(item.Source))
            return;

        _dispatcher.Invoke(() =>
        {
            // Play notification sound if enabled
            PlaySoundIfEnabled();

            // Add to history
            _history.Insert(0, item);
            if (_history.Count > MaxHistorySize)
                _history.RemoveAt(_history.Count - 1);

            // Add to active toasts (cap at max)
            if (ActiveNotifications.Count >= MaxActiveNotifications)
                DismissOldest();

            ActiveNotifications.Add(item);
            NotificationAdded?.Invoke(item);

            // Schedule auto-dismiss if duration is set
            var duration = item.Duration ?? GetDefaultDuration(item.Type);
            if (duration.HasValue)
                ScheduleDismiss(item.Id, duration.Value);
        });
    }

    public void Notify(
        string title,
        NotificationType type = NotificationType.Info,
        NotificationSource source = NotificationSource.System,
        string? message = null,
        string? relatedItemId = null,
        TimeSpan? duration = null)
    {
        var item = new NotificationItem
        {
            Title = title,
            Type = type,
            Source = source,
            Message = message,
            Icon = GetIconForType(type),
            RelatedItemId = relatedItemId,
            Duration = duration
        };
        Notify(item);
    }

    public void Dismiss(string notificationId)
    {
        _dispatcher.Invoke(() =>
        {
            var item = ActiveNotifications.FirstOrDefault(n => n.Id == notificationId);
            if (item is null) return;

            CancelTimer(notificationId);
            ActiveNotifications.Remove(item);
            NotificationDismissed?.Invoke(item);
        });
    }

    public void DismissAll()
    {
        _dispatcher.Invoke(() =>
        {
            foreach (var timer in _dismissTimers.Values)
                timer.Stop();
            _dismissTimers.Clear();

            var items = ActiveNotifications.ToList();
            ActiveNotifications.Clear();
            foreach (var item in items)
                NotificationDismissed?.Invoke(item);
        });
    }

    public void MarkAllAsRead()
    {
        _dispatcher.Invoke(() =>
        {
            foreach (var item in _history)
                item.IsRead = true;
        });
    }

    public void ClearHistory()
    {
        _dispatcher.Invoke(() => _history.Clear());
    }

    private void ScheduleDismiss(string notificationId, TimeSpan duration)
    {
        CancelTimer(notificationId);

        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Dismiss(notificationId);
        };
        _dismissTimers[notificationId] = timer;
        timer.Start();
    }

    private void CancelTimer(string notificationId)
    {
        if (_dismissTimers.Remove(notificationId, out var timer))
            timer.Stop();
    }

    private void DismissOldest()
    {
        if (ActiveNotifications.Count > 0)
        {
            var oldest = ActiveNotifications[0];
            CancelTimer(oldest.Id);
            ActiveNotifications.RemoveAt(0);
            NotificationDismissed?.Invoke(oldest);
        }
    }

    /// <summary>
    /// Default auto-dismiss duration by notification type.
    /// Returns null for Error (persistent until dismissed manually).
    /// </summary>
    private static TimeSpan? GetDefaultDuration(NotificationType type) => type switch
    {
        NotificationType.Info => TimeSpan.FromSeconds(5),
        NotificationType.Success => TimeSpan.FromSeconds(5),
        NotificationType.Warning => TimeSpan.FromSeconds(10),
        NotificationType.Progress => null,
        NotificationType.Error => null,
        _ => TimeSpan.FromSeconds(5)
    };

    /// <summary>
    /// Checks if notifications from the given source are enabled in settings.
    /// Uses synchronous cache to avoid async in hot path — settings are loaded once.
    /// </summary>
    private bool IsSourceEnabled(NotificationSource source)
    {
        // Get settings synchronously (already cached after first load)
        var settings = Task.Run(() => _settingsService.GetSettingsAsync()).GetAwaiter().GetResult();

        if (!settings.NotificationsEnabled)
            return false;

        return source switch
        {
            NotificationSource.Terminal => settings.NotifyTerminalEvents,
            NotificationSource.Git     => settings.NotifyGitEvents,
            NotificationSource.Process => settings.NotifyProcessEvents,
            NotificationSource.AI      => settings.NotifyAiEvents,
            NotificationSource.System  => settings.NotifySystemEvents,
            _                          => true
        };
    }

    private void PlaySoundIfEnabled()
    {
        try
        {
            var settings = Task.Run(() => _settingsService.GetSettingsAsync()).GetAwaiter().GetResult();
            if (settings.NotificationSoundEnabled)
                System.Media.SystemSounds.Asterisk.Play();
        }
        catch { }
    }

    private static string GetIconForType(NotificationType type) => type switch
    {
        NotificationType.Info => "\u2139",     // ℹ
        NotificationType.Success => "\u2705",  // ✅
        NotificationType.Warning => "\u26A0",  // ⚠
        NotificationType.Error => "\u274C",    // ❌
        NotificationType.Progress => "\u26A1", // ⚡
        _ => "\u2139"
    };
}
