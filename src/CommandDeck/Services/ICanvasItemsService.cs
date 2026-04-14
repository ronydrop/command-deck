using System;
using System.Collections.ObjectModel;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Services;

/// <summary>
/// Manages the collection of canvas items (terminals and widgets) and their spatial state.
/// Acts as the single source of truth for what is on the canvas.
/// </summary>
public interface ICanvasItemsService
{
    // ─── Collections ─────────────────────────────────────────────────────────

    /// <summary>All items currently on the canvas (terminals + widgets).</summary>
    ObservableCollection<CanvasItemViewModel> Items { get; }

    /// <summary>Filtered view of only terminal items — used by the sidebar.</summary>
    ObservableCollection<TerminalCanvasItemViewModel> TerminalItems { get; }

    /// <summary>The terminal that currently receives keyboard input.</summary>
    TerminalCanvasItemViewModel? ActiveTerminal { get; set; }

    // ─── Mutations ────────────────────────────────────────────────────────────

    /// <summary>Creates and registers a terminal item wrapping <paramref name="terminal"/>.</summary>
    TerminalCanvasItemViewModel AddTerminalItem(TerminalViewModel terminal);

    /// <summary>Creates and registers a widget item of the given type.</summary>
    WidgetCanvasItemViewModel AddWidgetItem(WidgetType type);

    /// <summary>Creates and registers a dedicated chat tile (supports multiple instances).</summary>
    ChatCanvasItemViewModel AddChatTile(double x = 40, double y = 40);

    /// <summary>Creates and registers a Code Editor tile.</summary>
    CodeEditorCanvasItemViewModel AddCodeEditorTile(double x = 40, double y = 40);

    /// <summary>Creates and registers a File Explorer tile.</summary>
    FileExplorerCanvasItemViewModel AddFileExplorerTile(double x = 40, double y = 40);

    /// <summary>Creates and registers a Browser tile.</summary>
    BrowserCanvasItemViewModel AddBrowserTile(double x = 40, double y = 40);

    /// <summary>Creates and registers an Activity Feed tile.</summary>
    ActivityFeedCanvasItemViewModel AddActivityFeedTile(double x = 40, double y = 40);

    /// <summary>Toggles a singleton widget: removes it if it exists, adds it if it doesn't.</summary>
    void ToggleWidgetItem(WidgetType type, double? fixedX = null, double? fixedY = null);

    /// <summary>Returns true if a widget of the given type is currently on the canvas.</summary>
    bool HasWidget(WidgetType type);

    /// <summary>Removes the item with the specified id from the canvas.</summary>
    void RemoveItem(string itemId);

    /// <summary>Updates the position of an item (ViewModel X/Y are updated automatically).</summary>
    void MoveItem(string itemId, double x, double y);

    /// <summary>Updates the size of an item.</summary>
    void ResizeItem(string itemId, double width, double height);

    /// <summary>Brings the specified item to the front (highest ZIndex).</summary>
    void BringToFront(string itemId);

    /// <summary>Sets the IsFocused flag on the given item and clears all others.</summary>
    void SetFocused(string? itemId);

    /// <summary>Removes all items from the canvas and resets layout state.</summary>
    void ClearAll();

    /// <summary>Adds a pre-built item (restored from saved layout) without recalculating position.</summary>
    void AddRestoredItem(CanvasItemViewModel item);

    /// <summary>Manually triggers a workspace-changed notification (auto-save + minimap refresh).</summary>
    void NotifyChanged();

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired whenever the collection or any item's spatial state changes.</summary>
    event Action? WorkspaceChanged;
}
