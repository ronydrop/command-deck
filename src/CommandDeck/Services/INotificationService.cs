using System.Collections.ObjectModel;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Manages application-wide notifications (toasts and history).
/// Any service or ViewModel can emit notifications through this.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Currently visible toast notifications (max 5 simultaneous).
    /// Bind this to the UI overlay.
    /// </summary>
    ObservableCollection<NotificationItem> ActiveNotifications { get; }

    /// <summary>
    /// History of recent notifications (last 50).
    /// </summary>
    ReadOnlyObservableCollection<NotificationItem> History { get; }

    /// <summary>
    /// Count of unread notifications.
    /// </summary>
    int UnreadCount { get; }

    /// <summary>
    /// Fired when a new notification is added.
    /// </summary>
    event Action<NotificationItem>? NotificationAdded;

    /// <summary>
    /// Fired when a notification is dismissed.
    /// </summary>
    event Action<NotificationItem>? NotificationDismissed;

    /// <summary>
    /// Emits a notification with full control over properties.
    /// </summary>
    void Notify(NotificationItem item);

    /// <summary>
    /// Convenience overload: emit a notification with common parameters.
    /// </summary>
    void Notify(
        string title,
        NotificationType type = NotificationType.Info,
        NotificationSource source = NotificationSource.System,
        string? message = null,
        string? relatedItemId = null,
        TimeSpan? duration = null);

    /// <summary>
    /// Dismiss a single notification by ID.
    /// </summary>
    void Dismiss(string notificationId);

    /// <summary>
    /// Dismiss all active notifications.
    /// </summary>
    void DismissAll();

    /// <summary>
    /// Mark all notifications as read, resetting <see cref="UnreadCount"/>.
    /// </summary>
    void MarkAllAsRead();

    /// <summary>
    /// Clear the notification history.
    /// </summary>
    void ClearHistory();
}
