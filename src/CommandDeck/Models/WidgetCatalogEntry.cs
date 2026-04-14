using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommandDeck.Models;

namespace CommandDeck.Models;

/// <summary>
/// Metadata entry for a built-in widget in the Widget Catalog.
/// Describes what the widget does and whether the user has it enabled.
/// </summary>
public class WidgetCatalogEntry
{
    /// <summary>Stable unique key, e.g. "note", "git", "codeeditor".</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Display name shown in the catalog UI.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Short description of the widget's purpose.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Emoji or icon character for the widget card.</summary>
    public string Icon { get; init; } = "🧩";

    /// <summary>Accent color hex for the widget card (Catppuccin Mocha palette).</summary>
    public string AccentColor { get; init; } = "#cba6f7";

    /// <summary>Category tag used for grouping in the catalog.</summary>
    public string Category { get; init; } = "Geral";

    /// <summary>Whether the widget is currently enabled by the user.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether this is a core widget that cannot be disabled.</summary>
    public bool IsCore { get; init; }

    /// <summary>The <see cref="WidgetType"/> this entry maps to (null for tiles with own VMs).</summary>
    [JsonIgnore]
    public WidgetType? WidgetType { get; init; }

    /// <summary>The <see cref="CanvasItemType"/> for tiles that bypass WidgetType (ChatWidget, CodeEditorWidget, etc.).</summary>
    [JsonIgnore]
    public CanvasItemType? CanvasItemType { get; init; }

    /// <summary>Preview hint shown below the description in the catalog card.</summary>
    public string PreviewHint { get; init; } = string.Empty;
}
