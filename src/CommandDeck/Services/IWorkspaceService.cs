using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Services;

/// <summary>
/// Manages the collection of canvas items (terminals and widgets) and their spatial state.
/// Acts as the single source of truth for what is on the canvas.
/// Also provides multi-workspace lifecycle (create, switch, delete, list).
/// </summary>
public interface IWorkspaceService
{
    // ─── Canvas Items ───────────────────────────────────────────────────────

    /// <summary>All items currently on the canvas (terminals + widgets).</summary>
    ObservableCollection<CanvasItemViewModel> Items { get; }

    /// <summary>Filtered view of only terminal items — used by the sidebar.</summary>
    ObservableCollection<TerminalCanvasItemViewModel> TerminalItems { get; }

    /// <summary>The terminal that currently receives keyboard input.</summary>
    TerminalCanvasItemViewModel? ActiveTerminal { get; set; }

    /// <summary>Creates and registers a terminal item wrapping <paramref name="terminal"/>.</summary>
    TerminalCanvasItemViewModel AddTerminalItem(TerminalViewModel terminal);

    /// <summary>Creates and registers a widget item of the given type.</summary>
    WidgetCanvasItemViewModel AddWidgetItem(WidgetType type);

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

    /// <summary>Fired whenever the collection or any item's spatial state changes.</summary>
    event Action? WorkspaceChanged;

    // ─── Multi-Workspace Lifecycle ──────────────────────────────────────────

    /// <summary>The currently active workspace model. Null until initialized.</summary>
    WorkspaceModel? CurrentWorkspace { get; }

    /// <summary>Creates a new workspace and persists it. Does NOT switch to it.</summary>
    Task<WorkspaceModel> CreateWorkspaceAsync(string name, string color = "#CBA6F7", string icon = "FolderIcon");

    /// <summary>Saves the current canvas state, then loads the target workspace.</summary>
    Task SwitchWorkspaceAsync(string workspaceId);

    /// <summary>Lists all persisted workspaces (active one first, then by last accessed).</summary>
    Task<IReadOnlyList<WorkspaceModel>> ListWorkspacesAsync();

    /// <summary>Deletes a workspace by id. Cannot delete the active workspace.</summary>
    Task<bool> DeleteWorkspaceAsync(string workspaceId);

    /// <summary>Renames the specified workspace.</summary>
    Task RenameWorkspaceAsync(string workspaceId, string newName);

    /// <summary>Updates the color of the specified workspace.</summary>
    Task UpdateWorkspaceColorAsync(string workspaceId, string newColor);

    /// <summary>Initializes the workspace system, loading the active workspace or creating one.</summary>
    Task InitializeAsync();

    /// <summary>Saves the current workspace canvas state to persistence.</summary>
    Task SaveCurrentAsync();

    /// <summary>Updates the in-memory camera state for the current workspace (persisted on next save).</summary>
    void UpdateCamera(CameraStateModel camera);

    /// <summary>Fired when the active workspace changes.</summary>
    event Action<WorkspaceModel>? ActiveWorkspaceChanged;
}
