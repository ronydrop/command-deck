using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CommandDeck.Models;

/// <summary>Type discriminator for canvas items.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CanvasItemType
{
    Terminal,
    GitWidget,
    ProcessWidget,
    ShortcutWidget,
    NoteWidget,
    ImageWidget,
    KanbanWidget,
    ChatWidget,
    SystemMonitorWidget,
    TokenCounterWidget,
    PomodoroWidget
}

/// <summary>Logical widget category used by CanvasItemFactory.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WidgetType
{
    Git,
    Process,
    Shortcut,
    Note,
    Image,
    Kanban,
    Chat,
    SystemMonitor,
    TokenCounter,
    Pomodoro
}

/// <summary>
/// Serializable data model for a single item on the spatial canvas.
/// Not an ObservableObject — kept plain for clean JSON round-trips.
/// </summary>
public class CanvasItemModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public CanvasItemType Type { get; set; }
    public double X { get; set; } = 0;
    public double Y { get; set; } = 0;
    public double Width { get; set; } = 700;
    public double Height { get; set; } = 480;
    public int ZIndex { get; set; } = 0;

    /// <summary>Position index when displayed in tiled layout mode (-1 = unset).</summary>
    public int TiledIndex { get; set; } = -1;

    /// <summary>Arbitrary key/value pairs for type-specific metadata (shellType, projectPath, etc.).</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
