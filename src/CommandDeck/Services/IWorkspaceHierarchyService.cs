using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Manages the logical hierarchy of workspaces, groups, projects, and terminals.
/// Acts as the orchestrator between <see cref="IWorkspaceService"/> (canvas items),
/// <see cref="IProjectService"/> (project data), and the TreeView UI.
/// 
/// ARCHITECTURE:
///   - The hierarchy is a tree of <see cref="WorkspaceNodeModel"/> instances.
///   - Root nodes represent workspaces (currently one "default" workspace).
///   - Groups are logical containers (e.g. "Backend", "Frontend").
///   - Project nodes reference <see cref="Project"/> models.
///   - Terminal nodes reference canvas items and sessions.
///   - All mutations go through this service to maintain consistency.
/// 
/// PERSISTENCE:
///   - The hierarchy structure is stored in <see cref="WorkspaceModel"/> via
///     the <see cref="CanvasItemModel.Metadata"/> dictionary. Each canvas item
///     stores "hierarchyParentId" to indicate its parent node.
///   - Group definitions are stored in workspace metadata at the WorkspaceModel level.
///   - Full persistence uses <see cref="ILayoutPersistenceService"/>.
/// </summary>
public interface IWorkspaceHierarchyService
{
    // ─── Events ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when any structural change occurs (add/remove/move of nodes).
    /// The TreeView VM listens to this to refresh the display.
    /// </summary>
    event Action? HierarchyChanged;

    /// <summary>
    /// Fired when a node is activated (double-clicked or Enter pressed).
    /// Parameter is the activated node.
    /// </summary>
    event Action<WorkspaceNodeModel>? NodeActivated;

    // ─── Query ─────────────────────────────────────────────────────────────

    /// <summary>Returns all root nodes (workspaces).</summary>
    IReadOnlyList<WorkspaceNodeModel> GetRootNodes();

    /// <summary>Finds a node by its Id. Returns null if not found.</summary>
    WorkspaceNodeModel? FindNode(string nodeId);

    /// <summary>
    /// Finds a terminal node by canvas item Id.
    /// Used to link canvas interactions back to the tree.
    /// </summary>
    WorkspaceNodeModel? FindTerminalByCanvasItemId(string canvasItemId);

    /// <summary>
    /// Finds a terminal node by session Id.
    /// Used to link terminal session events back to the tree.
    /// </summary>
    WorkspaceNodeModel? FindTerminalBySessionId(string sessionId);

    /// <summary>
    /// Gets all terminal nodes under a specific parent (workspace, group, or project).
    /// </summary>
    IReadOnlyList<WorkspaceNodeModel> GetTerminalNodes(string parentNodeId);

    /// <summary>
    /// Gets all groups directly under a workspace.
    /// </summary>
    IReadOnlyList<WorkspaceNodeModel> GetGroups(string workspaceId);

    /// <summary>
    /// Gets the path of nodes from root to the specified node.
    /// Useful for breadcrumb display.
    /// </summary>
    IReadOnlyList<WorkspaceNodeModel> GetPathToNode(string nodeId);

    // ─── Workspace operations ──────────────────────────────────────────────

    /// <summary>
    /// Creates a new workspace root node with the given name.
    /// Returns the created node.
    /// </summary>
    WorkspaceNodeModel CreateWorkspace(string name = "New Workspace");

    /// <summary>
    /// Renames a workspace node.
    /// </summary>
    Task RenameWorkspaceAsync(string workspaceId, string newName);

    /// <summary>
    /// Deletes a workspace and all its descendants.
    /// Removes all associated canvas items via IWorkspaceService.
    /// </summary>
    Task DeleteWorkspaceAsync(string workspaceId);

    /// <summary>
    /// Switches the "active" workspace. Only one workspace is active at a time.
    /// The active workspace determines what is shown on the canvas.
    /// </summary>
    Task SwitchWorkspaceAsync(string workspaceId);

    /// <summary>Gets the currently active workspace node.</summary>
    WorkspaceNodeModel? GetActiveWorkspace();

    // ─── Group operations ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a new group node under the specified parent.
    /// Parent must be a Workspace or another Group.
    /// </summary>
    WorkspaceNodeModel CreateGroup(string parentNodeId, string name, string? color = null);

    /// <summary>
    /// Renames a group node.
    /// </summary>
    Task RenameGroupAsync(string groupId, string newName);

    /// <summary>
    /// Changes the color of a group.
    /// </summary>
    Task SetGroupColorAsync(string groupId, string color);

    /// <summary>
    /// Deletes a group. Children are promoted to the group's parent.
    /// </summary>
    Task DeleteGroupAsync(string groupId);

    // ─── Project node operations ───────────────────────────────────────────

    /// <summary>
    /// Creates a project node under the specified parent (workspace or group).
    /// Links to the existing <see cref="Project"/> model.
    /// </summary>
    WorkspaceNodeModel CreateProjectNode(
        string parentNodeId,
        Project project);

    /// <summary>
    /// Removes the project node from the hierarchy.
    /// Does NOT delete the <see cref="Project"/> model itself.
    /// </summary>
    Task RemoveProjectNodeAsync(string projectNodeId);

    /// <summary>
    /// Moves a project node to a different parent.
    /// </summary>
    Task MoveProjectNodeAsync(string projectNodeId, string newParentNodeId);

    // ─── Terminal node operations ──────────────────────────────────────────

    /// <summary>
    /// Registers a terminal in the hierarchy under the specified parent.
    /// Called when a terminal is added to the canvas.
    /// The parent can be a Workspace, Group, or Project node.
    /// </summary>
    WorkspaceNodeModel AddTerminalNode(
        string parentNodeId,
        string sessionId,
        string canvasItemId,
        string title,
        ShellType shellType,
        string? projectId = null);

    /// <summary>
    /// Removes a terminal node from the hierarchy.
    /// Called when a terminal is removed from the canvas.
    /// Does NOT close the terminal session — that's handled by the caller.
    /// </summary>
    Task RemoveTerminalNodeAsync(string terminalNodeId);

    /// <summary>
    /// Updates the display title of a terminal node.
    /// Called when the terminal title changes (e.g. from ANSI escape sequences).
    /// </summary>
    Task UpdateTerminalTitleAsync(string terminalNodeId, string newTitle);

    // ─── Generic move (drag-drop) ──────────────────────────────────────────

    /// <summary>
    /// Moves a node from its current parent to a new parent.
    /// Validates the operation based on node types (see <see cref="IsValidMove"/>).
    /// Updates CanvasItemModel.Metadata["hierarchyParentId"] for terminal nodes.
    /// Fires <see cref="HierarchyChanged"/>.
    /// </summary>
    /// <param name="nodeId">The node to move.</param>
    /// <param name="newParentNodeId">The target parent node.</param>
    /// <param name="insertIndex">Optional index within the parent's children. Null = append.</param>
    /// <exception cref="InvalidOperationException">If the move is invalid.</exception>
    Task MoveNodeAsync(string nodeId, string newParentNodeId, int? insertIndex = null);

    /// <summary>
    /// Validates whether a move operation would be legal.
    /// Does NOT perform the move — just checks constraints.
    /// </summary>
    bool IsValidMove(string nodeId, string newParentNodeId);

    // ─── Bulk operations ───────────────────────────────────────────────────

    /// <summary>
    /// Loads the hierarchy from persisted data.
    /// Reconstructs the tree from WorkspaceModel + CanvasItemModel.Metadata.
    /// Called once on startup.
    /// </summary>
    Task LoadHierarchyAsync();

    /// <summary>
    /// Persists the current hierarchy state.
    /// Called automatically after mutations (debounced).
    /// Also callable explicitly for save-on-demand.
    /// </summary>
    Task SaveHierarchyAsync();

    /// <summary>
    /// Reorders children within a parent node.
    /// Used for manual reordering via drag-drop within the same parent.
    /// </summary>
    Task ReorderChildrenAsync(string parentNodeId, int oldIndex, int newIndex);

    /// <summary>
    /// Removes all terminal nodes from the hierarchy (e.g. on workspace reset).
    /// </summary>
    Task ClearAllTerminalsAsync(string workspaceId);

    // ─── Statistics ────────────────────────────────────────────────────────

    /// <summary>
    /// Gets counts for the active workspace: { groups, projects, terminals }.
    /// </summary>
    (int Groups, int Projects, int Terminals) GetActiveWorkspaceCounts();
}
