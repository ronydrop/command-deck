using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

public class WorkspaceTreeService : IWorkspaceTreeService
{
    private static readonly string DataPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "DevWorkspaceHub", "workspace-tree.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<WorkspaceNodeModel> _roots = new();

    public IReadOnlyList<WorkspaceNodeModel> RootNodes => _roots;

    // ─── Persistence ─────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        if (!File.Exists(DataPath)) return;
        await _lock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(DataPath);
            var loaded = JsonSerializer.Deserialize<List<WorkspaceNodeModel>>(json, JsonOpts);
            if (loaded is not null)
            {
                _roots.Clear();
                _roots.AddRange(loaded);
            }
        }
        catch { }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
            var json = JsonSerializer.Serialize(_roots, JsonOpts);
            await File.WriteAllTextAsync(DataPath, json);
        }
        finally { _lock.Release(); }
    }

    // ─── Add ─────────────────────────────────────────────────────────────────

    public WorkspaceNodeModel AddGroup(string name, string color = "#6C7086", string? parentId = null)
    {
        var node = new WorkspaceNodeModel
        {
            NodeType = WorkspaceNodeType.Group,
            Name = name,
            Color = color,
            IconKey = "FolderIcon",
            ParentId = parentId
        };
        Insert(node, parentId);
        return node;
    }

    public WorkspaceNodeModel AddProject(string name, string path, string? parentId = null)
    {
        var node = new WorkspaceNodeModel
        {
            NodeType = WorkspaceNodeType.Project,
            Name = name,
            Color = "#89B4FA",
            IconKey = "FolderIcon",
            ProjectPath = path,
            ParentId = parentId
        };
        Insert(node, parentId);
        return node;
    }

    public WorkspaceNodeModel AddTerminalNode(string name, string canvasItemId, string? parentId = null)
    {
        var node = new WorkspaceNodeModel
        {
            NodeType = WorkspaceNodeType.Terminal,
            Name = name,
            Color = "#A6E3A1",
            IconKey = "TerminalIcon",
            LinkedCanvasItemId = canvasItemId,
            ParentId = parentId
        };
        Insert(node, parentId);
        return node;
    }

    // ─── Mutate ───────────────────────────────────────────────────────────────

    public void Rename(string nodeId, string newName)
    {
        var node = FindById(nodeId);
        if (node is not null) node.Name = newName;
    }

    public void SetColor(string nodeId, string hexColor)
    {
        var node = FindById(nodeId);
        if (node is not null) node.Color = hexColor;
    }

    public void Move(string nodeId, string? newParentId, int sortOrder = 0)
    {
        var node = FindById(nodeId);
        if (node is null) return;

        RemoveFromParent(node);
        node.ParentId = newParentId;
        node.SortOrder = sortOrder;
        Insert(node, newParentId);
    }

    public void Remove(string nodeId)
    {
        var node = FindById(nodeId);
        if (node is null) return;
        RemoveFromParent(node);
    }

    // ─── Query ────────────────────────────────────────────────────────────────

    public WorkspaceNodeModel? FindById(string nodeId)
        => FindInList(_roots, nodeId);

    public IReadOnlyList<WorkspaceNodeModel> Search(string query)
    {
        var result = new List<WorkspaceNodeModel>();
        if (string.IsNullOrWhiteSpace(query)) return result;
        SearchInList(_roots, query.ToLower(), result);
        return result;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void Insert(WorkspaceNodeModel node, string? parentId)
    {
        if (parentId is null)
        {
            _roots.Add(node);
            return;
        }
        var parent = FindById(parentId);
        parent?.Children.Add(node);
    }

    private void RemoveFromParent(WorkspaceNodeModel node)
    {
        if (node.ParentId is null)
        {
            _roots.Remove(node);
            return;
        }
        var parent = FindById(node.ParentId);
        parent?.Children.Remove(node);
    }

    private static WorkspaceNodeModel? FindInList(IEnumerable<WorkspaceNodeModel> list, string id)
    {
        foreach (var n in list)
        {
            if (n.Id == id) return n;
            var found = FindInList(n.Children, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static void SearchInList(IEnumerable<WorkspaceNodeModel> list, string query, List<WorkspaceNodeModel> result)
    {
        foreach (var n in list)
        {
            if (n.Name.ToLower().Contains(query)) result.Add(n);
            SearchInList(n.Children, query, result);
        }
    }
}
