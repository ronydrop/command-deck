using System.Collections.Concurrent;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Thread-safe in-process pub/sub event bus with wildcard subscriptions and a ring-buffer history.
/// <para>
/// Pattern matching rules:
/// <list type="bullet">
///   <item>"*" matches every event.</item>
///   <item>"Terminal_*" matches any event whose type name starts with "Terminal_".</item>
///   <item>"Terminal_OutputReceived" matches only that exact type.</item>
///   <item>"custom:my-channel" matches Custom events whose Channel equals "my-channel".</item>
/// </list>
/// </para>
/// <para>
/// Ring-buffer: keeps the last <see cref="RingBufferCapacity"/> events in memory so late
/// subscribers can call <see cref="GetHistory"/> to replay missed events.
/// </para>
/// </summary>
public sealed class EventBusService : IEventBusService, IDisposable
{
    private const int RingBufferCapacity = 200;

    private readonly ConcurrentDictionary<string, SubscriberEntry> _subscribers = new();
    private readonly object _historyLock = new();
    private readonly BusEventRecord[] _ringBuffer = new BusEventRecord[RingBufferCapacity];
    private int _ringHead; // next write index
    private int _ringCount; // number of valid entries
    private int _sequence; // monotonically increasing sequence number

    // ─── Publish ─────────────────────────────────────────────────────────────

    public void Publish(BusEvent busEvent)
    {
        ArgumentNullException.ThrowIfNull(busEvent);

        // Add to ring-buffer
        RecordToHistory(busEvent);

        // Dispatch to matching subscribers
        foreach (var entry in _subscribers.Values)
        {
            if (!entry.IsActive) continue;
            if (Matches(entry.Pattern, busEvent))
            {
                try { entry.Handler(busEvent); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EventBus] Handler '{entry.SubscriptionId}' threw: {ex.Message}");
                }
            }
        }
    }

    public void Publish<T>(BusEventType type, T payload, string? source = null, string? channel = null)
        => Publish(BusEvent.Create(type, payload, source, channel));

    public void Publish(BusEventType type, string? source = null, string? channel = null)
        => Publish(BusEvent.Create(type, source, channel));

    // ─── Subscribe ───────────────────────────────────────────────────────────

    public BusSubscription Subscribe(string pattern, Action<BusEvent> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid().ToString("N")[..8];
        var entry = new SubscriberEntry(id, pattern, handler);
        _subscribers[id] = entry;

        return new BusSubscription(() =>
        {
            if (_subscribers.TryGetValue(id, out var e)) e.IsActive = false;
            _subscribers.TryRemove(id, out _);
        }, pattern);
    }

    public BusSubscription Subscribe<T>(BusEventType type, Action<T, BusEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe(type.ToString(), evt =>
        {
            if (evt.Payload is T typed)
                handler(typed, evt);
        });
    }

    // ─── History ─────────────────────────────────────────────────────────────

    public IReadOnlyList<BusEventRecord> GetHistory(BusEventType? type = null, int maxCount = 50)
    {
        lock (_historyLock)
        {
            var count = Math.Min(_ringCount, maxCount);
            var result = new List<BusEventRecord>(count);

            // Walk backwards from last written to oldest
            for (int i = 0; i < _ringCount && result.Count < maxCount; i++)
            {
                int idx = (_ringHead - 1 - i + RingBufferCapacity) % RingBufferCapacity;
                var record = _ringBuffer[idx];
                if (record is null) break;
                if (type is null || record.Event.Type == type.Value)
                    result.Add(record);
            }

            return result;
        }
    }

    public int HistoryCount
    {
        get { lock (_historyLock) return _ringCount; }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private void RecordToHistory(BusEvent evt)
    {
        lock (_historyLock)
        {
            var seq = ++_sequence;
            _ringBuffer[_ringHead] = new BusEventRecord { Event = evt, SequenceNumber = seq };
            _ringHead = (_ringHead + 1) % RingBufferCapacity;
            if (_ringCount < RingBufferCapacity) _ringCount++;
        }
    }

    /// <summary>
    /// Returns true if <paramref name="pattern"/> matches <paramref name="evt"/>.
    /// </summary>
    private static bool Matches(string pattern, BusEvent evt)
    {
        if (pattern == "*") return true;

        var typeName = evt.Type.ToString();

        // Custom channel pattern: "custom:my-channel"
        if (pattern.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
        {
            if (evt.Type != BusEventType.Custom) return false;
            var ch = pattern["custom:".Length..];
            return string.Equals(evt.Channel, ch, StringComparison.OrdinalIgnoreCase);
        }

        // Wildcard suffix: "Terminal_*"
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1]; // strip trailing *
            return typeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Exact match
        return string.Equals(typeName, pattern, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        foreach (var entry in _subscribers.Values)
            entry.IsActive = false;
        _subscribers.Clear();
    }

    // ─── Inner class ─────────────────────────────────────────────────────────

    private sealed class SubscriberEntry(string id, string pattern, Action<BusEvent> handler)
    {
        public string SubscriptionId { get; } = id;
        public string Pattern { get; } = pattern;
        public Action<BusEvent> Handler { get; } = handler;
        public volatile bool IsActive = true;
    }
}
