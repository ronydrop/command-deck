using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Manages multi-workspace CRUD and lifecycle: create, switch, delete, rename, and persistence.
/// </summary>
public interface IWorkspaceLifecycleService
{
    // ─── State ────────────────────────────────────────────────────────────────

    /// <summary>The currently active workspace model. Null until initialized.</summary>
    WorkspaceModel? CurrentWorkspace { get; }

    // ─── Lifecycle Operations ─────────────────────────────────────────────────

    /// <summary>Initializes the workspace system, loading the active workspace or creating one.</summary>
    Task InitializeAsync();

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

    /// <summary>Saves the current workspace canvas state to persistence.</summary>
    Task SaveCurrentAsync();

    /// <summary>Updates the in-memory camera state for the current workspace (persisted on next save).</summary>
    void UpdateCamera(CameraStateModel camera);

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired when the active workspace changes.</summary>
    event Action<WorkspaceModel>? ActiveWorkspaceChanged;
}
