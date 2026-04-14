using System.Collections.Concurrent;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Thread-safe implementation of <see cref="ITileContextService"/>.
/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> as the backing store
/// and publishes change events through <see cref="IEventBusService"/>.
/// </summary>
public sealed class TileContextService : ITileContextService
{
    private readonly IEventBusService _eventBus;
    private readonly ConcurrentDictionary<string, TileContextEntry> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string? Key, Action<TileContextChangedArgs> Handler)> _subscribers = new();
    private readonly object _subscriberLock = new();

    public event Action<TileContextChangedArgs>? ContextChanged;

    public TileContextService(IEventBusService eventBus)
    {
        _eventBus = eventBus;
    }

    // ─── Write ────────────────────────────────────────────────────────────────

    public void Set(string key, object? value, string? sourceTileId = null, string? sourceLabel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var previous = _store.TryGetValue(key, out var old) ? old : null;

        var entry = new TileContextEntry
        {
            Key = key,
            Value = value,
            SourceTileId = sourceTileId,
            SourceLabel = sourceLabel,
            UpdatedAt = DateTime.UtcNow
        };

        _store[key] = entry;

        var args = new TileContextChangedArgs { Key = key, Entry = entry, Previous = previous };

        // Fire event bus notification
        _eventBus.Publish(new BusEvent
        {
            Type = BusEventType.Custom,
            Channel = $"context:{key}",
            Payload = args,
            Source = sourceTileId
        });

        // Notify direct subscribers
        NotifySubscribers(key, args);

        ContextChanged?.Invoke(args);
    }

    public void Remove(string key)
    {
        if (!_store.TryRemove(key, out var old)) return;

        var args = new TileContextChangedArgs { Key = key, Entry = null, Previous = old };

        _eventBus.Publish(new BusEvent
        {
            Type = BusEventType.Custom,
            Channel = $"context:{key}",
            Payload = args,
            Source = null
        });

        NotifySubscribers(key, args);
        ContextChanged?.Invoke(args);
    }

    // ─── Read ─────────────────────────────────────────────────────────────────

    public TileContextEntry? Get(string key)
        => _store.TryGetValue(key, out var entry) ? entry : null;

    public T? GetValue<T>(string key, T? defaultValue = default)
    {
        if (!_store.TryGetValue(key, out var entry)) return defaultValue;
        try { return (T?)entry.Value; }
        catch { return defaultValue; }
    }

    public IReadOnlyDictionary<string, TileContextEntry> GetAll()
        => _store;

    // ─── Subscribe ────────────────────────────────────────────────────────────

    public IDisposable Subscribe(string key, Action<TileContextChangedArgs> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);

        lock (_subscriberLock)
            _subscribers.Add((key, handler));

        return new SubscriptionToken(() =>
        {
            lock (_subscriberLock)
                _subscribers.RemoveAll(s => s.Key == key && s.Handler == handler);
        });
    }

    public IDisposable SubscribeAll(Action<TileContextChangedArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_subscriberLock)
            _subscribers.Add((null, handler)); // null key = wildcard

        return new SubscriptionToken(() =>
        {
            lock (_subscriberLock)
                _subscribers.RemoveAll(s => s.Key is null && s.Handler == handler);
        });
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private void NotifySubscribers(string key, TileContextChangedArgs args)
    {
        List<Action<TileContextChangedArgs>> toNotify;
        lock (_subscriberLock)
        {
            toNotify = _subscribers
                .Where(s => s.Key is null || string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Handler)
                .ToList();
        }

        foreach (var handler in toNotify)
        {
            try { handler(args); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TileContext] Subscriber threw: {ex.Message}");
            }
        }
    }

    // ─── Inner class ─────────────────────────────────────────────────────────

    private sealed class SubscriptionToken(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            dispose();
        }
    }
}
