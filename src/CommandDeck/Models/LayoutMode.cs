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
    Tiled
}
