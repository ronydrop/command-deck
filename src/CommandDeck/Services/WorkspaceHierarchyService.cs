using System.Collections.Concurrent;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Default implementation of <see cref="IWorkspaceHierarchyService"/>.
/// Manages an in-memory tree of <see cref="WorkspaceNodeModel"/> instances,
/// coordinates with <see cref="IWorkspaceService"/> and <see cref="ILayoutPersistenceService"/>
/// for canvas and persistence concerns.
/// </summary>
public sealed class WorkspaceHierarchyService : IWorkspaceHierarchyService
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ILayoutPersistenceService _layoutPersistence;
    private readonly IPersistenceService _persistence;

    private readonly List<WorkspaceNodeModel> _rootNodes = new();
    private readonly ConcurrentDictionary<string, WorkspaceNodeModel> _allNodes = new(StringComparer.OrdinalIgnoreCase);

    private WorkspaceNodeModel? _activeWorkspace;

    /// <inheritdoc />
    public event Action? HierarchyChanged;

    /// <inheritdoc />
    public event Action<WorkspaceNodeModel>? NodeActivated;

    public WorkspaceHierarchyService(
        IWorkspaceService workspaceService,
        ILayoutPersistenceService layoutPersistence,
        IPersistenceService persistence)
    {
        _workspaceService = workspaceService;
        _layoutPersistence = layoutPersistence;
        _persistence = persistence;
    }

    // ─── Query ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<WorkspaceNodeModel> GetRootNodes() => _rootNodes.AsReadOnly();

    /// <inheritdoc />
    public WorkspaceNodeModel? FindNode(string nodeId) =>
        _allNodes.TryGetValue(nodeId, out var node) ? node : null;

    /// <inheritdoc />
    public WorkspaceNodeModel? FindTerminalByCanvasItemId(string canvasItemId) =>
        _allNodes.Values.FirstOrDefault(n =>
            n.NodeType == WorkspaceNodeType.Terminal &&
            string.Equals(n.CanvasItemId, canvasItemId, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public WorkspaceNodeModel? FindTerminalBySessionId(string sessionId) =>
        _allNodes.Values.FirstOrDefault(n =>
            n.NodeType == WorkspaceNodeType.Terminal &&
            string.Equals(n.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IReadOnlyList<WorkspaceNodeModel> GetTerminalNodes(string parentNodeId) =>
        FindNode(parentNodeId)?.Children
            .Where(n => n.NodeType == WorkspaceNodeType.Terminal)
            .ToList()
        ?? new List<WorkspaceNodeModel>();

    /// <inheritdoc />
    public IReadOnlyList<WorkspaceNodeModel> GetGroups(string workspaceId) =>
        FindNode(workspaceId)?.Children
            .Where(n => n.NodeType == WorkspaceNodeType.Group)
            .ToList()
        ?? new List<WorkspaceNodeModel>();

    /// <inheritdoc />
    public IReadOnlyList<WorkspaceNodeModel> GetPathToNode(string nodeId)
    {
        var node = FindNode(nodeId);
        if (node is null) return Array.Empty<WorkspaceNodeModel>();

        var path = new List<WorkspaceNodeModel>();
        var current = node;
        while (current is not null)
        {
            path.Add(current);
            current = current.Parent;
        }
        path.Reverse();
        return path;
    }

    // ─── Workspace operations ──────────────────────────────────────────────

    /// <inheritdoc />
    public WorkspaceNodeModel CreateWorkspace(string name = "New Workspace")
    {
        var node = new WorkspaceNodeModel
        {
            Name = name,
            NodeType = WorkspaceNodeType.Workspace,
            Icon = "\uE8FC",
            WorkspaceId = Guid.NewGuid().ToString("N")
        };

        _rootNodes.Add(node);
        _allNodes[node.Id] = node;
        _activeWorkspace ??= node;

        HierarchyChanged?.Invoke();
        return node;
    }

    /// <inheritdoc />
    public Task RenameWorkspaceAsync(string workspaceId, string newName)
    {
        var node = FindNode(workspaceId);
        if (node is not null)
        {
            node.Name = newName;
            HierarchyChanged?.Invoke();
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DeleteWorkspaceAsync(string workspaceId)
    {
        var node = FindNode(workspaceId);
        if (node is null) return;

        // Remove all terminal nodes first
        await ClearAllTerminalsAsync(workspaceId);

        // Remove all descendant nodes from lookup
        RemoveDescendantsFromLookup(node);

        _rootNodes.Remove(node);
        _allNodes.TryRemove(node.Id, out _);

        if (_activeWorkspace?.Id == workspaceId)
            _activeWorkspace = _rootNodes.FirstOrDefault();

        HierarchyChanged?.Invoke();
    }

    /// <inheritdoc />
    public Task SwitchWorkspaceAsync(string workspaceId)
    {
        var node = FindNode(workspaceId);
        if (node?.NodeType == WorkspaceNodeType.Workspace)
        {
            _activeWorkspace = node;
            HierarchyChanged?.Invoke();
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public WorkspaceNodeModel? GetActiveWorkspace() => _activeWorkspace;

    // ─── Group operations ──────────────────────────────────────────────────

    /// <inheritdoc />
    public WorkspaceNodeModel CreateGroup(string parentNodeId, string name, string? color = null)
    {
        var parent = FindNode(parentNodeId);
        if (parent is null)
            throw new ArgumentException($"Parent node '{parentNodeId}' not found.", nameof(parentNodeId));

        if (parent.NodeType is not (WorkspaceNodeType.Workspace or WorkspaceNodeType.Group))
            throw new InvalidOperationException("Groups can only be created under workspaces or other groups.");

        var node = new WorkspaceNodeModel
        {
            Name = name,
            NodeType = WorkspaceNodeType.Group,
            Icon = "\uE7B3",
            Color = color ?? "#89B4FA",
            WorkspaceId = parent.NodeType == WorkspaceNodeType.Workspace ? parent.WorkspaceId : parent.WorkspaceId,
            Parent = parent
        };

        parent.Children.Add(node);
        _allNodes[node.Id] = node;

        HierarchyChanged?.Invoke();
        return node;
    }

    /// <inheritdoc />
    public Task RenameGroupAsync(string groupId, string newName)
    {
        var node = FindNode(groupId);
        if (node is not null)
        {
            node.Name = newName;
            HierarchyChanged?.Invoke();
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetGroupColorAsync(string groupId, string color)
    {
        var node = FindNode(groupId);
        if (node is not null)
        {
            node.Color = color;
            HierarchyChanged?.Invoke();
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DeleteGroupAsync(string groupId)
    {
        var node = FindNode(groupId);
        if (node is null || node.Parent is null) return;

        // Promote children to group's parent
        var promotedChildren = node.Children.ToList();
        foreach (var child in promotedChildren)
        {
            child.Parent = node.Parent;
            node.Parent.Children.Add(child);
        }

        // Remove group from lookup
        RemoveDescendantsFromLookup(node);
        node.Parent.Children.Remove(node);
        _allNodes.TryRemove(node.Id, out _);

        HierarchyChanged?.Invoke();
        await Task.CompletedTask;
    }

    // ─── Project node operations ───────────────────────────────────────────

    /// <inheritdoc />
    public WorkspaceNodeModel CreateProjectNode(string parentNodeId, Project project)
    {
        var parent = FindNode(parentNodeId);
        if (parent is null)
            throw new ArgumentException($"Parent node '{parentNodeId}' not found.", nameof(parentNodeId));

        if (parent.NodeType is not (WorkspaceNodeType.Workspace or WorkspaceNodeType.Group))
            throw new InvalidOperationException("Project nodes can only be created under workspaces or groups.");

        var node = new WorkspaceNodeModel
        {
            Name = project.Name,
            NodeType = WorkspaceNodeType.Project,
            Icon = string.IsNullOrEmpty(project.Icon) ? "\uE8D2" : project.Icon,
            Color = string.IsNullOrEmpty(project.Color) ? "#A6E3A1" : project.Color,
            Subtitle = project.Path,
            ProjectId = project.Id,
            WorkspaceId = parent.NodeType == WorkspaceNodeType.Workspace ? parent.WorkspaceId : parent.WorkspaceId,
            Parent = parent
        };

        parent.Children.Add(node);
        _allNodes[node.Id] = node;

        HierarchyChanged?.Invoke();
        return node;
    }

    /// <inheritdoc />
    public Task RemoveProjectNodeAsync(string projectNodeId)
    {
        var node = FindNode(projectNodeId);
        if (node is null || node.Parent is null) return Task.CompletedTask;

        // Remove children first
        RemoveDescendantsFromLookup(node);
        node.Parent.Children.Remove(node);
        _allNodes.TryRemove(node.Id, out _);

        HierarchyChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task MoveProjectNodeAsync(string projectNodeId, string newParentNodeId)
    {
        await MoveNodeAsync(projectNodeId, newParentNodeId);
    }

    // ─── Terminal node operations ──────────────────────────────────────────

    /// <inheritdoc />
    public WorkspaceNodeModel AddTerminalNode(
        string parentNodeId,
        string sessionId,
        string canvasItemId,
        string title,
        ShellType shellType,
        string? projectId = null)
    {
        var parent = FindNode(parentNodeId);
        if (parent is null)
            throw new ArgumentException($"Parent node '{parentNodeId}' not found.", nameof(parentNodeId));

        if (!parent.CanAcceptChildren)
            throw new InvalidOperationException("Parent node cannot accept terminal children.");

        var icon = shellType switch
        {
            ShellType.PowerShell => "\uE756",
            ShellType.Cmd => "\uE756",
            ShellType.Wsl => "\uE945",
            ShellType.Bash => "\uE756",
            _ => "\uE756"
        };

        var node = new WorkspaceNodeModel
        {
            Name = title,
            NodeType = WorkspaceNodeType.Terminal,
            Icon = icon,
            SessionId = sessionId,
            CanvasItemId = canvasItemId,
            ProjectId = projectId,
            WorkspaceId = parent.NodeType == WorkspaceNodeType.Workspace ? parent.WorkspaceId : parent.WorkspaceId,
            Parent = parent
        };

        parent.Children.Add(node);
        _allNodes[node.Id] = node;

        HierarchyChanged?.Invoke();
        return node;
    }

    /// <inheritdoc />
    public Task RemoveTerminalNodeAsync(string terminalNodeId)
    {
        var node = FindNode(terminalNodeId);
        if (node is null || node.Parent is null) return Task.CompletedTask;

        node.Parent.Children.Remove(node);
        _allNodes.TryRemove(node.Id, out _);

        HierarchyChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateTerminalTitleAsync(string terminalNodeId, string newTitle)
    {
        var node = FindNode(terminalNodeId);
        if (node is not null)
        {
            node.Name = newTitle;
        }
        return Task.CompletedTask;
    }

    // ─── Generic move (drag-drop) ──────────────────────────────────────────

    /// <inheritdoc />
    public async Task MoveNodeAsync(string nodeId, string newParentNodeId, int? insertIndex = null)
    {
        if (!IsValidMove(nodeId, newParentNodeId))
            throw new InvalidOperationException($"Cannot move node '{nodeId}' to '{newParentNodeId}'.");

        var node = FindNode(nodeId);
        var newParent = FindNode(newParentNodeId);
        if (node is null || newParent is null) return;

        var oldParent = node.Parent;

        // Remove from old parent
        oldParent?.Children.Remove(node);

        // Add to new parent
        if (insertIndex.HasValue && insertIndex.Value >= 0 && insertIndex.Value <= newParent.Children.Count)
            newParent.Children.Insert(insertIndex.Value, node);
        else
            newParent.Children.Add(node);

        node.Parent = newParent;

        // Update workspace reference
        var workspace = GetRootWorkspace(newParent);
        if (workspace is not null)
            UpdateWorkspaceIdRecursive(node, workspace.WorkspaceId);

        // Update canvas item metadata
        if (node.NodeType == WorkspaceNodeType.Terminal && node.CanvasItemId is not null)
        {
            var canvasItem = _workspaceService.Items.FirstOrDefault(i => i.Id == node.CanvasItemId);
            if (canvasItem is not null)
            {
                canvasItem.Metadata["hierarchyParentId"] = newParentNodeId;
            }
        }

        HierarchyChanged?.Invoke();
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool IsValidMove(string nodeId, string newParentNodeId)
    {
        if (nodeId == newParentNodeId) return false;

        var node = FindNode(nodeId);
        var newParent = FindNode(newParentNodeId);
        if (node is null || newParent is null) return false;

        // Workspaces cannot be moved
        if (node.NodeType == WorkspaceNodeType.Workspace) return false;

        // Terminals cannot have children, so they can't be drop targets
        if (newParent.NodeType == WorkspaceNodeType.Terminal) return false;

        // Prevent moving a node into its own descendants
        var current = newParent;
        while (current is not null)
        {
            if (current.Id == nodeId) return false;
            current = current.Parent;
        }

        // Terminal nodes can only go under Workspace, Group, or Project
        if (node.NodeType == WorkspaceNodeType.Terminal)
            return newParent.NodeType is WorkspaceNodeType.Workspace
                                     or WorkspaceNodeType.Group
                                     or WorkspaceNodeType.Project;

        // Group nodes can go under Workspace or Group
        if (node.NodeType == WorkspaceNodeType.Group)
            return newParent.NodeType is WorkspaceNodeType.Workspace or WorkspaceNodeType.Group;

        // Project nodes can go under Workspace or Group
        if (node.NodeType == WorkspaceNodeType.Project)
            return newParent.NodeType is WorkspaceNodeType.Workspace or WorkspaceNodeType.Group;

        return false;
    }

    // ─── Bulk operations ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task LoadHierarchyAsync()
    {
        // Load all workspaces from SQLite and create hierarchy nodes
        try
        {
            var workspaces = await _persistence.ListWorkspacesAsync();

            if (workspaces.Count > 0)
            {
                foreach (var ws in workspaces)
                {
                    var existing = FindNode(ws.Id);
                    if (existing is not null)
                    {
                        existing.Name = ws.Name;
                        existing.Color = ws.Color;
                        if (ws.IsActive) _activeWorkspace = existing;
                    }
                    else
                    {
                        var node = new WorkspaceNodeModel
                        {
                            Id = ws.Id,
                            Name = ws.Name,
                            NodeType = WorkspaceNodeType.Workspace,
                            Color = ws.Color,
                            Icon = ws.Icon,
                            WorkspaceId = ws.Id
                        };
                        _rootNodes.Add(node);
                        _allNodes[node.Id] = node;
                        if (ws.IsActive) _activeWorkspace = node;
                    }
                }
            }
        }
        catch
        {
            // If DB loading fails, fall through to create default
        }

        // Ensure there's at least a default workspace
        if (_rootNodes.Count == 0)
        {
            CreateWorkspace("Workspace Principal");
        }

        // Fallback: if no workspace is active, activate the first one
        _activeWorkspace ??= _rootNodes.FirstOrDefault();

        HierarchyChanged?.Invoke();
    }

    /// <inheritdoc />
    public async Task SaveHierarchyAsync()
    {
        // Persistence delegated to ILayoutPersistenceService
        // Called automatically after mutations (debounced by callers)
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ReorderChildrenAsync(string parentNodeId, int oldIndex, int newIndex)
    {
        var parent = FindNode(parentNodeId);
        if (parent is null) return;

        if (oldIndex < 0 || oldIndex >= parent.Children.Count) return;
        if (newIndex < 0 || newIndex >= parent.Children.Count) return;
        if (oldIndex == newIndex) return;

        var child = parent.Children[oldIndex];
        parent.Children.RemoveAt(oldIndex);

        // Adjust index after removal
        var insertAt = newIndex > oldIndex ? newIndex - 1 : newIndex;
        parent.Children.Insert(insertAt, child);

        HierarchyChanged?.Invoke();
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ClearAllTerminalsAsync(string workspaceId)
    {
        var workspace = FindNode(workspaceId);
        if (workspace is null) return;

        var terminals = GetAllNodes(workspace)
            .Where(n => n.NodeType == WorkspaceNodeType.Terminal)
            .ToList();

        foreach (var terminal in terminals)
        {
            terminal.Parent?.Children.Remove(terminal);
            _allNodes.TryRemove(terminal.Id, out _);
        }

        HierarchyChanged?.Invoke();
        await Task.CompletedTask;
    }

    // ─── Statistics ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public (int Groups, int Projects, int Terminals) GetActiveWorkspaceCounts()
    {
        if (_activeWorkspace is null) return (0, 0, 0);

        var allNodes = GetAllNodes(_activeWorkspace).Skip(1).ToList(); // skip workspace itself
        return (
            allNodes.Count(n => n.NodeType == WorkspaceNodeType.Group),
            allNodes.Count(n => n.NodeType == WorkspaceNodeType.Project),
            allNodes.Count(n => n.NodeType == WorkspaceNodeType.Terminal)
        );
    }

    // ─── Private helpers ───────────────────────────────────────────────────

    private static IEnumerable<WorkspaceNodeModel> GetAllNodes(WorkspaceNodeModel root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in GetAllNodes(child))
                yield return descendant;
        }
    }

    private static WorkspaceNodeModel? GetRootWorkspace(WorkspaceNodeModel node)
    {
        var current = node;
        while (current.Parent is not null)
            current = current.Parent;
        return current.NodeType == WorkspaceNodeType.Workspace ? current : null;
    }

    private static void UpdateWorkspaceIdRecursive(WorkspaceNodeModel node, string? workspaceId)
    {
        node.WorkspaceId = workspaceId;
        foreach (var child in node.Children)
            UpdateWorkspaceIdRecursive(child, workspaceId);
    }

    private void RemoveDescendantsFromLookup(WorkspaceNodeModel node)
    {
        foreach (var child in node.Children.ToList())
        {
            RemoveDescendantsFromLookup(child);
            _allNodes.TryRemove(child.Id, out _);
        }
    }
}
