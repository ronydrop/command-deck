using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommandDeck.Models;

namespace CommandDeck.Models;

/// <summary>Split orientation for a pane node.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SplitOrientation { Horizontal, Vertical }

/// <summary>
/// Binary tree node for the Split Pane layout.
/// Either a <see cref="SplitPaneNode"/> (has children) or a <see cref="LeafPaneNode"/> (holds one tile).
/// </summary>
public abstract class PaneNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

/// <summary>Interior node: splits space between two child panes.</summary>
public class SplitPaneNode : PaneNode
{
    public SplitOrientation Orientation { get; set; } = SplitOrientation.Horizontal;

    /// <summary>Fraction of space given to the first child (0..1). Default 0.5.</summary>
    public double Ratio { get; set; } = 0.5;

    public PaneNode? First { get; set; }
    public PaneNode? Second { get; set; }
}

/// <summary>Leaf node: holds the ID of a canvas tile.</summary>
public class LeafPaneNode : PaneNode
{
    public string? ItemId { get; set; }
}

/// <summary>
/// The root model for the split pane layout tree.
/// Serialized alongside the workspace.
/// </summary>
public class SplitPaneLayout
{
    public PaneNode? Root { get; set; }
}
