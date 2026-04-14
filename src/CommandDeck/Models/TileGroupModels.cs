using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CommandDeck.Models;

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
