using System;
using System.Text.Json.Serialization;

namespace CommandDeck.Models;

// ─── Event Types ────────────────────────────────────────────────────────────

/// <summary>
/// Discriminated event type for the internal Event Bus.
/// Follows a "domain.action" convention so wildcard subscriptions can match "domain.*".
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BusEventType
{
    // ─── Terminal events ───────────────────────────────────────────────────
    Terminal_OutputReceived,
    Terminal_SessionCreated,
    Terminal_SessionExited,
    Terminal_TitleChanged,
    Terminal_CommandExecuted,

    // ─── Canvas events ─────────────────────────────────────────────────────
    Canvas_ItemAdded,
    Canvas_ItemRemoved,
    Canvas_ItemMoved,
    Canvas_ItemResized,
    Canvas_ItemFocused,
    Canvas_LayoutChanged,

    // ─── Chat events ──────────────────────────────────────────────────────
    Chat_MessageSent,
    Chat_MessageReceived,
    Chat_ContextInjected,

    // ─── Project events ───────────────────────────────────────────────────
    Project_Switched,
    Project_Opened,
    Project_Closed,

    // ─── Git events ───────────────────────────────────────────────────────
    Git_BranchChanged,
    Git_StatusChanged,
    Git_CommitCreated,

    // ─── AI events ────────────────────────────────────────────────────────
    AI_ProviderChanged,
    AI_RequestStarted,
    AI_RequestCompleted,
    AI_RequestFailed,
    AI_OrbActivated,

    // ─── Browser events ───────────────────────────────────────────────────
    Browser_Navigated,
    Browser_ElementSelected,
    Browser_ContextSent,

    // ─── System events ────────────────────────────────────────────────────
    System_Notification,
    System_SettingsChanged,
    System_ThemeChanged,

    // ─── Custom / extension events ────────────────────────────────────────
    Custom,
}

// ─── Core Models ────────────────────────────────────────────────────────────

/// <summary>
/// A single event published on the Event Bus.
/// Carries a typed payload (boxed as <see cref="object"/>).
/// </summary>
public class BusEvent
{
    /// <summary>Unique identifier for this event instance.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Type discriminator for subscriber matching and wildcards.</summary>
    public BusEventType Type { get; init; }

    /// <summary>
    /// Custom channel name for <see cref="BusEventType.Custom"/> events,
    /// or a sub-topic override like "terminal.{sessionId}".
    /// </summary>
    public string? Channel { get; init; }

    /// <summary>Payload data. Cast to the expected type in the handler.</summary>
    public object? Payload { get; init; }

    /// <summary>Source that published this event (canvas item id, service name, etc.).</summary>
    public string? Source { get; init; }

    /// <summary>UTC timestamp when the event was published.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    // ─── Factory helpers ──────────────────────────────────────────────────

    public static BusEvent Create<T>(BusEventType type, T payload, string? source = null, string? channel = null)
        => new() { Type = type, Payload = payload, Source = source, Channel = channel };

    public static BusEvent Create(BusEventType type, string? source = null, string? channel = null)
        => new() { Type = type, Source = source, Channel = channel };
}

/// <summary>
/// Represents an active subscription returned by <see cref="Services.IEventBusService.Subscribe"/>.
/// Dispose to unsubscribe.
/// </summary>
public class BusSubscription : IDisposable
{
    private readonly Action _unsubscribe;
    private bool _disposed;

    /// <summary>Unique id for this subscription.</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>The event type pattern this subscription matches.</summary>
    public string Pattern { get; init; } = "*";

    internal BusSubscription(Action unsubscribe, string pattern)
    {
        _unsubscribe = unsubscribe;
        Pattern = pattern;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _unsubscribe();
    }
}

/// <summary>
/// A single entry in the ring-buffer history of published events.
/// </summary>
public class BusEventRecord
{
    public BusEvent Event { get; init; } = null!;
    public int SequenceNumber { get; init; }
}
