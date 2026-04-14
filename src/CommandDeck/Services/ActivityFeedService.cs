using System;
using System.Collections.Generic;
using System.Linq;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Thread-safe in-memory activity feed with a maximum capacity of 500 entries.
/// </summary>
public sealed class ActivityFeedService : IActivityFeedService
{
    private const int MaxEntries = 500;
    private readonly object _lock = new();
    private readonly LinkedList<ActivityEntry> _entries = new();

    public event Action<ActivityEntry>? EntryAdded;

    private static readonly Dictionary<ActivityEntryType, (string Icon, string Color)> Defaults = new()
    {
        [ActivityEntryType.Terminal] = ("⬛", "#a6e3a1"),
        [ActivityEntryType.Project]  = ("📂", "#89b4fa"),
        [ActivityEntryType.Git]      = ("🌿", "#fab387"),
        [ActivityEntryType.AI]       = ("💬", "#cba6f7"),
        [ActivityEntryType.Editor]   = ("⌨", "#89b4fa"),
        [ActivityEntryType.Browser]  = ("🌐", "#89b4fa"),
        [ActivityEntryType.Widget]   = ("🧩", "#94e2d5"),
        [ActivityEntryType.System]   = ("⚙", "#a6adc8"),
    };

    public void Log(ActivityEntryType type, string title, string? detail = null,
                    string? source = null, string? icon = null, string? accentColor = null)
    {
        var (defaultIcon, defaultColor) = Defaults.GetValueOrDefault(type, ("📋", "#a6adc8"));

        var entry = new ActivityEntry
        {
            Type = type,
            Title = title,
            Detail = detail,
            Source = source,
            Icon = icon ?? defaultIcon,
            AccentColor = accentColor ?? defaultColor,
            Timestamp = DateTime.Now
        };

        lock (_lock)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > MaxEntries)
                _entries.RemoveLast();
        }

        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<ActivityEntry> GetRecent(int maxCount = 100, ActivityEntryType? filter = null)
    {
        lock (_lock)
        {
            var q = _entries.AsEnumerable();
            if (filter.HasValue) q = q.Where(e => e.Type == filter.Value);
            return q.Take(maxCount).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }
}
