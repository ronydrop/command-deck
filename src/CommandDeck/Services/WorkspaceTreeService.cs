using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

public class WorkspaceTreeService : IWorkspaceTreeService
{
    private static readonly string DataPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "CommandDeck", "workspace-tree.json");

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceTreeService] Failed to load workspace tree from disk: {ex.Message}");
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(DataPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_roots, JsonOpts);
            // Atomic write: write to temp file, then move in place
            var tempPath = DataPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, DataPath, overwrite: true);
        }
        finally { _lock.Release(); }
    }

    // ─── Add ─────────────────────────────────────────────────────────────────

    public WorkspaceNodeModel AddProject(string name, string path, string? parentId = null)
    {
        _lock.Wait();
        try
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
        finally { _lock.Release(); }
    }

    public WorkspaceNodeModel AddTerminalNode(string name, string canvasItemId, string? parentId = null)
    {
        _lock.Wait();
        try
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
        finally { _lock.Release(); }
    }

    // ─── Mutate ───────────────────────────────────────────────────────────────

    public void Rename(string nodeId, string newName)
    {
        _lock.Wait();
        try
        {
            var node = FindById(nodeId);
            if (node is not null) node.Name = newName;
        }
        finally { _lock.Release(); }
    }

    public void SetColor(string nodeId, string hexColor)
    {
        _lock.Wait();
        try
        {
            var node = FindById(nodeId);
            if (node is not null) node.Color = hexColor;
        }
        finally { _lock.Release(); }
    }

    public void Move(string nodeId, string? newParentId, int sortOrder = 0)
    {
        _lock.Wait();
        try
        {
            var node = FindById(nodeId);
            if (node is null) return;

            // Prevent circular reference: cannot move a node into its own descendant
            if (newParentId is not null && IsDescendantOf(nodeId, newParentId))
                throw new InvalidOperationException(
                    $"Cannot move node '{nodeId}' under its own descendant '{newParentId}'");

            RemoveFromParent(node);
            node.ParentId = newParentId;
            node.SortOrder = sortOrder;
            Insert(node, newParentId);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Returns true if <paramref name="candidateChildId"/> is a descendant of <paramref name="ancestorId"/>.</summary>
    private bool IsDescendantOf(string ancestorId, string candidateChildId)
    {
        var ancestor = FindById(ancestorId);
        if (ancestor is null) return false;
        return FindInList(ancestor.Children, candidateChildId) is not null;
    }

    public void Remove(string nodeId)
    {
        _lock.Wait();
        try
        {
            RemoveInternal(nodeId);
        }
        finally { _lock.Release(); }
    }

    private void RemoveInternal(string nodeId)
    {
        var node = FindById(nodeId);
        if (node is null) return;
        // Cascade-delete: remove all children first to avoid orphans
        foreach (var child in node.Children.ToList())
            RemoveInternal(child.Id);
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

    // ─── Validation ──────────────────────────────────────────────────────────

    public async Task<int> ValidateAndCleanAsync(IReadOnlySet<string> validCanvasItemIds)
    {
        await _lock.WaitAsync();
        int removeCount;
        try
        {
            var allNodes = new List<WorkspaceNodeModel>();
            CollectAll(_roots, allNodes);

            var toRemove = new List<string>();

            // 1. Terminal nodes whose LinkedCanvasItemId is missing or not in valid set
            foreach (var node in allNodes)
            {
                if (node.NodeType == WorkspaceNodeType.Terminal
                    && (string.IsNullOrEmpty(node.LinkedCanvasItemId)
                        || !validCanvasItemIds.Contains(node.LinkedCanvasItemId)))
                {
                    toRemove.Add(node.Id);
                }
            }

            // 2. Nodes with ParentId pointing to non-existent parent (move to root)
            foreach (var node in allNodes)
            {
                if (node.ParentId is not null && FindInList(_roots, node.ParentId) is null)
                {
                    node.ParentId = null;
                    if (!_roots.Contains(node))
                        _roots.Add(node);
                }
            }

            // 3. Remove duplicates (same Id appearing more than once)
            var seen = new HashSet<string>();
            foreach (var node in allNodes)
            {
                if (!seen.Add(node.Id) && !toRemove.Contains(node.Id))
                    toRemove.Add(node.Id);
            }

            // Execute removals (use internal to avoid deadlock with _lock)
            foreach (var id in toRemove)
                RemoveInternal(id);

            removeCount = toRemove.Count;
        }
        finally { _lock.Release(); }

        if (removeCount > 0)
        {
            await SaveAsync();
            System.Diagnostics.Debug.WriteLine(
                $"[WorkspaceTreeService] Cleaned {removeCount} orphan node(s) from workspace tree");
        }

        return removeCount;
    }

    private static void CollectAll(IEnumerable<WorkspaceNodeModel> list, List<WorkspaceNodeModel> result)
    {
        foreach (var n in list)
        {
            result.Add(n);
            CollectAll(n.Children, result);
        }
    }
}
