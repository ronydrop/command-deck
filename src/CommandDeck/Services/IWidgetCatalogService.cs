using System.Collections.Generic;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Registry of all built-in widgets. Users can toggle widgets on/off;
/// the menu and catalog UI reflect only what is enabled.
/// </summary>
public interface IWidgetCatalogService
{
    /// <summary>Returns all registered widget entries (enabled and disabled).</summary>
    IReadOnlyList<WidgetCatalogEntry> All { get; }

    /// <summary>Returns only the enabled widget entries.</summary>
    IReadOnlyList<WidgetCatalogEntry> Enabled { get; }

    /// <summary>Returns the entry for the given key, or null if not found.</summary>
    WidgetCatalogEntry? Get(string key);

    /// <summary>Enables or disables a widget. Persists the preference immediately.</summary>
    void SetEnabled(string key, bool enabled);

    /// <summary>Resets all widgets to their default (enabled) state.</summary>
    void ResetToDefaults();

    /// <summary>Returns true if a widget with the given key is currently enabled.</summary>
    bool IsEnabled(string key);

    /// <summary>Fired whenever an entry's enabled state changes.</summary>
    event System.Action? CatalogChanged;
}
