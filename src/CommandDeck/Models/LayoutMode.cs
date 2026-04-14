using System.Text.Json.Serialization;

namespace CommandDeck.Models;

/// <summary>
/// Layout mode for the terminal workspace area.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LayoutMode
{
    /// <summary>Free-form canvas with drag, resize, pan and zoom.</summary>
    FreeCanvas,

    /// <summary>Auto-arranged tiled grid layout.</summary>
    Tiled,

    /// <summary>Binary split-pane layout with adjustable splitters.</summary>
    SplitPane,

    /// <summary>Fixed 3×3 bento-box grid with 8 drop-target slots and a central block catalog.</summary>
    Bento
}
