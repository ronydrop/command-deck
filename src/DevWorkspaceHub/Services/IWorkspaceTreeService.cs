using System.Collections.Generic;
using System.Threading.Tasks;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Manages the hierarchical workspace tree (groups → projects → terminals).
/// Persists to JSON alongside the existing layout JSON.
/// </summary>
public interface IWorkspaceTreeService
{
    IReadOnlyList<WorkspaceNodeModel> RootNodes { get; }

    Task LoadAsync();
    Task SaveAsync();

    WorkspaceNodeModel AddGroup(string name, string color = "#6C7086", string? parentId = null);
    WorkspaceNodeModel AddProject(string name, string path, string? parentId = null);
    WorkspaceNodeModel AddTerminalNode(string name, string canvasItemId, string? parentId = null);

    void Rename(string nodeId, string newName);
    void SetColor(string nodeId, string hexColor);
    void Move(string nodeId, string? newParentId, int sortOrder = 0);
    void Remove(string nodeId);

    WorkspaceNodeModel? FindById(string nodeId);
    IReadOnlyList<WorkspaceNodeModel> Search(string query);
}
