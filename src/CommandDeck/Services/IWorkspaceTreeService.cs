using System.Collections.Generic;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Manages the hierarchical workspace tree (groups → projects → terminals).
/// Persists to JSON alongside the existing layout JSON.
/// </summary>
public interface IWorkspaceTreeService
{
    IReadOnlyList<WorkspaceNodeModel> RootNodes { get; }

    Task LoadAsync();
    Task SaveAsync();

    WorkspaceNodeModel AddProject(string name, string path, string? parentId = null);
    WorkspaceNodeModel AddTerminalNode(string name, string canvasItemId, string? parentId = null);

    void Rename(string nodeId, string newName);
    void SetColor(string nodeId, string hexColor);
    void Move(string nodeId, string? newParentId, int sortOrder = 0);
    void Remove(string nodeId);

    WorkspaceNodeModel? FindById(string nodeId);
    IReadOnlyList<WorkspaceNodeModel> Search(string query);

    /// <summary>
    /// Removes orphan terminal nodes whose LinkedCanvasItemId is not in the valid set.
    /// Also removes nodes with invalid ParentId references.
    /// Returns the number of nodes removed.
    /// </summary>
    Task<int> ValidateAndCleanAsync(IReadOnlySet<string> validCanvasItemIds);
}
