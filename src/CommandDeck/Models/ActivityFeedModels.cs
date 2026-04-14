using System;
using System.Text.Json.Serialization;

namespace CommandDeck.Models;

/// <summary>Type of activity entry for grouping and filtering in the feed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActivityEntryType
{
    Terminal,
    Project,
    Git,
    AI,
    Editor,
    Browser,
    Widget,
    System
}

/// <summary>
/// A single entry in the Activity Feed.
/// Represents a notable action or event in the application.
/// </summary>
public class ActivityEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public ActivityEntryType Type { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public string? Source { get; init; }
    public string Icon { get; init; } = "📋";
    public string? AccentColor { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>Human-readable relative timestamp (e.g., "2 min atrás").</summary>
    public string RelativeTime
    {
        get
        {
            var diff = DateTime.Now - Timestamp;
            if (diff.TotalSeconds < 60) return "agora";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min atrás";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h atrás";
            return Timestamp.ToString("dd/MM HH:mm");
        }
    }
}
