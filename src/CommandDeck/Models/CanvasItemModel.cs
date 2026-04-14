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
    PomodoroWidget,
    CodeEditorWidget,
    FileExplorerWidget,
    BrowserWidget,
    ActivityFeedWidget,
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
    Pomodoro,
    CodeEditor,
    FileExplorer,
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

    /// <summary>Index of the Bento slot this item occupies (0..7, reading order skipping center). -1 when not assigned.</summary>
    public int BentoSlotIndex { get; set; } = -1;

    // ─── Tile customization (Fase 3.4) ───────────────────────────────────────

    /// <summary>Custom accent color hex (e.g. "#cba6f7"). Null = use theme default.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Custom label shown in the card titlebar (overrides default title).</summary>
    public string? TileLabel { get; set; }

    /// <summary>Whether the titlebar is hidden (content-only mode).</summary>
    public bool HideTitlebar { get; set; }

    /// <summary>Border corner radius override. -1 = theme default.</summary>
    public double TileBorderRadius { get; set; } = -1;

    // ─── Connection targets (Fase 3.3) ───────────────────────────────────────

    /// <summary>IDs of tiles this tile is connected to with a Bézier line.</summary>
    public List<string> ConnectionTargetIds { get; set; } = new();

    /// <summary>Arbitrary key/value pairs for type-specific metadata (shellType, projectPath, etc.).</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
