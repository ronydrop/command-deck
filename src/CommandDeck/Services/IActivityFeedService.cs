using System;
using System.Collections.Generic;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Central log of notable application events — terminal sessions, project switches,
/// AI messages, file opens, etc. Displayed by the Activity Feed widget.
/// </summary>
public interface IActivityFeedService
{
    /// <summary>Logs a new entry to the feed.</summary>
    void Log(ActivityEntryType type, string title, string? detail = null,
             string? source = null, string? icon = null, string? accentColor = null);

    /// <summary>Returns the most recent entries (newest first).</summary>
    IReadOnlyList<ActivityEntry> GetRecent(int maxCount = 100, ActivityEntryType? filter = null);

    /// <summary>Clears all entries from the feed.</summary>
    void Clear();

    /// <summary>Total number of entries since last clear.</summary>
    int Count { get; }

    /// <summary>Fired whenever a new entry is added.</summary>
    event Action<ActivityEntry>? EntryAdded;
}
