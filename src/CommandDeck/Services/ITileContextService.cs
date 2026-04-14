using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Shared context store for cross-tile communication following a produces/consumes pattern.
/// <para>
/// Any tile can <see cref="Set"/> a key/value pair, and any other tile can <see cref="Get"/>
/// or <see cref="Subscribe"/> to changes. Backed by <see cref="IEventBusService"/> so all
/// context changes also appear in the event stream.
/// </para>
/// </summary>
public interface ITileContextService
{
    // ─── Write ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets a context value identified by <paramref name="key"/>.
    /// Fires a <see cref="BusEventType.Custom"/> event on channel "context:{key}".
    /// </summary>
    void Set(string key, object? value, string? sourceTileId = null, string? sourceLabel = null);

    /// <summary>
    /// Removes a context entry by key.
    /// </summary>
    void Remove(string key);

    // ─── Read ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current <see cref="TileContextEntry"/> for <paramref name="key"/>,
    /// or null if not set.
    /// </summary>
    TileContextEntry? Get(string key);

    /// <summary>
    /// Returns the current value for <paramref name="key"/> cast to <typeparamref name="T"/>,
    /// or <paramref name="defaultValue"/> if not set or not castable.
    /// </summary>
    T? GetValue<T>(string key, T? defaultValue = default);

    /// <summary>
    /// Returns all currently set context entries.
    /// </summary>
    IReadOnlyDictionary<string, TileContextEntry> GetAll();

    // ─── Subscribe ────────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to changes for a specific <paramref name="key"/>.
    /// The handler receives the new and previous <see cref="TileContextEntry"/>.
    /// Dispose the returned token to unsubscribe.
    /// </summary>
    IDisposable Subscribe(string key, Action<TileContextChangedArgs> handler);

    /// <summary>
    /// Subscribes to changes for ALL context keys.
    /// Dispose to unsubscribe.
    /// </summary>
    IDisposable SubscribeAll(Action<TileContextChangedArgs> handler);

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired whenever any context entry changes (set or removed).</summary>
    event Action<TileContextChangedArgs>? ContextChanged;
}
