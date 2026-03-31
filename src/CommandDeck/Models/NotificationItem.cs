using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommandDeck.Models;

/// <summary>
/// Type of notification, determines visual styling.
/// </summary>
public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error,
    Progress
}

/// <summary>
/// Source that originated the notification.
/// </summary>
public enum NotificationSource
{
    System,
    Terminal,
    Git,
    Process,
    AI
}

/// <summary>
/// Represents a single notification item displayed as a toast or in the history.
/// </summary>
public partial class NotificationItem : ObservableObject
{
    /// <summary>
    /// Unique identifier for this notification.
    /// </summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Notification severity/type — controls color and icon.
    /// </summary>
    [ObservableProperty]
    private NotificationType _type = NotificationType.Info;

    /// <summary>
    /// Which subsystem originated this notification.
    /// </summary>
    public NotificationSource Source { get; init; } = NotificationSource.System;

    /// <summary>
    /// Short title (e.g. "Build concluido").
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Optional longer description.
    /// </summary>
    [ObservableProperty]
    private string? _message;

    /// <summary>
    /// Icon glyph or emoji for the notification.
    /// </summary>
    public string Icon { get; init; } = "\u2139"; // ℹ

    /// <summary>
    /// When the notification was created.
    /// </summary>
    public DateTime CreatedAt { get; } = DateTime.Now;

    /// <summary>
    /// How long the toast stays visible. Null = persistent until dismissed.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Whether the user has seen/acknowledged this notification.
    /// </summary>
    [ObservableProperty]
    private bool _isRead;

    /// <summary>
    /// Optional action executed when the user clicks the notification body.
    /// </summary>
    public IRelayCommand? ActionCommand { get; init; }

    /// <summary>
    /// Optional ID linking to the related terminal, process, or project.
    /// </summary>
    public string? RelatedItemId { get; init; }
}
