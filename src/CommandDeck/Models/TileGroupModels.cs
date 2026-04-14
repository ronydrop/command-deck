using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CommandDeck.Models;

/// <summary>
/// A named group of canvas tiles. Members are moved together when any member is dragged.
/// Groups have a label and an accent color shown as a badge on the card border.
/// </summary>
public class TileGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Display label shown on members' group badge.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Hex color for the group badge and border accent.</summary>
    public string Color { get; set; } = "#89b4fa";

    /// <summary>IDs of tiles that belong to this group.</summary>
    public List<string> MemberIds { get; set; } = new();
}

/// <summary>
/// A saved layout template: a snapshot of tile types and relative positions.
/// Can be applied to a fresh canvas to restore a known arrangement.
/// </summary>
public class LayoutTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "📐";
    public bool IsBuiltIn { get; set; }
    public List<LayoutTemplateItem> Items { get; set; } = new();
}

/// <summary>
/// A single tile entry in a <see cref="LayoutTemplate"/>.
/// Positions are stored as fractions of the viewport (0..1) for resolution-independence.
/// </summary>
public class LayoutTemplateItem
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CanvasItemType Type { get; set; }
    public double RelativeX { get; set; }
    public double RelativeY { get; set; }
    public double RelativeWidth { get; set; }
    public double RelativeHeight { get; set; }
}
