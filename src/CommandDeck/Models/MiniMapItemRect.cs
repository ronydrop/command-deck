namespace CommandDeck.Models;

/// <summary>
/// Represents a single item rectangle projected into mini-map coordinate space.
/// Used by MiniMapViewModel to expose a flat, bindable list for the MiniMapControl.
/// </summary>
public sealed class MiniMapItemRect
{
    /// <summary>Left position in mini-map pixels.</summary>
    public double X { get; init; }

    /// <summary>Top position in mini-map pixels.</summary>
    public double Y { get; init; }

    /// <summary>Width in mini-map pixels.</summary>
    public double Width { get; init; }

    /// <summary>Height in mini-map pixels.</summary>
    public double Height { get; init; }

    /// <summary>True when the underlying canvas item is a Terminal; false for widgets.</summary>
    public bool IsTerminal { get; init; }

    /// <summary>True when the underlying canvas item is an AI terminal session.</summary>
    public bool IsAiSession { get; init; }

    /// <summary>True when the underlying canvas item is currently selected.</summary>
    public bool IsSelected { get; init; }
}
