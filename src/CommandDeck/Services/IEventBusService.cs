using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Internal pub/sub event bus for communication between canvas tiles and services.
/// Supports wildcard subscriptions ("*"), domain-level wildcards ("Terminal_*"),
/// and a ring-buffer of recent events for late subscribers to replay.
/// </summary>
public interface IEventBusService
{
    // ─── Publish ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Publishes an event to all matching subscribers.
    /// Thread-safe; dispatches handlers on the caller's thread.
    /// </summary>
    void Publish(BusEvent busEvent);

    /// <summary>
    /// Convenience overload: publish a typed event with payload.
    /// </summary>
    void Publish<T>(BusEventType type, T payload, string? source = null, string? channel = null);

    /// <summary>
    /// Convenience overload: publish an event with no payload.
    /// </summary>
    void Publish(BusEventType type, string? source = null, string? channel = null);

    // ─── Subscribe ───────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to events matching <paramref name="pattern"/>.
    /// <para>Pattern examples:</para>
    /// <list type="bullet">
    ///   <item>"*" — receives ALL events</item>
    ///   <item>"Terminal_*" — receives all Terminal_* events</item>
    ///   <item>"Terminal_OutputReceived" — receives only this exact type</item>
    ///   <item>"custom:my-channel" — receives Custom events on channel "my-channel"</item>
    /// </list>
    /// Dispose the returned <see cref="BusSubscription"/> to unsubscribe.
    /// </summary>
    BusSubscription Subscribe(string pattern, Action<BusEvent> handler);

    /// <summary>
    /// Subscribes to a single <see cref="BusEventType"/> with a strongly-typed payload handler.
    /// The handler is only invoked when the payload is of type <typeparamref name="T"/>.
    /// Dispose to unsubscribe.
    /// </summary>
    BusSubscription Subscribe<T>(BusEventType type, Action<T, BusEvent> handler);

    // ─── History ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns recent events from the ring-buffer (newest first), optionally filtered by type.
    /// Useful for tiles that want to replay events they missed before subscribing.
    /// </summary>
    IReadOnlyList<BusEventRecord> GetHistory(BusEventType? type = null, int maxCount = 50);

    /// <summary>
    /// Number of events in the ring-buffer.
    /// </summary>
    int HistoryCount { get; }
}
