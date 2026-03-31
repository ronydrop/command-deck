using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the workspace hierarchy sidebar.
/// Owns the flat + tree display of WorkspaceNodeModels.
/// Raises NodeActivated so the View can pan the camera to the linked canvas item.
/// </summary>
public partial class WorkspaceTreeViewModel : ObservableObject
{
    private readonly IWorkspaceTreeService _treeService;

    public ObservableCollection<WorkspaceTreeNodeViewModel> RootNodes { get; } = new();

    [ObservableProperty] private WorkspaceTreeNodeViewModel? _selectedNode;

    /// <summary>Raised when the user activates a node — canvas should center on it.</summary>
    public event Action<string>? NodeActivated; // payload: canvasItemId

    public WorkspaceTreeViewModel(IWorkspaceTreeService treeService)
    {
        _treeService = treeService;
    }

    public async Task LoadAsync()
    {
        await _treeService.LoadAsync();
        Rebuild();
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void StartRenameNode()
    {
        if (SelectedNode is null) return;
        SelectedNode.IsEditing = true;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CommitRenameNode(WorkspaceTreeNodeViewModel? node)
    {
        if (node is null) return;
        node.IsEditing = false;
        if (!string.IsNullOrWhiteSpace(node.EditingName))
            await RenameNode(node.Model.Id, node.EditingName);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ChangeNodeColor(string hexColor)
    {
        if (SelectedNode is null) return;
        _treeService.SetColor(SelectedNode.Model.Id, hexColor);
        await _treeService.SaveAsync();
        Rebuild();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task AddGroup()
    {
        try
        {
            var node = _treeService.AddGroup("Novo Grupo");
            await _treeService.SaveAsync();
            Rebuild();
            // Auto-select the new node and enter edit mode
            SelectAndEdit(node.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceTree] AddGroup failed: {ex}");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DeleteSelected()
    {
        try
        {
            if (SelectedNode is null) return;
            _treeService.Remove(SelectedNode.Model.Id);
            await _treeService.SaveAsync();
            Rebuild();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceTree] DeleteSelected failed: {ex}");
        }
    }

    [RelayCommand]
    private void ActivateSelected()
    {
        if (SelectedNode?.Model.LinkedCanvasItemId is string id)
            NodeActivated?.Invoke(id);
    }

    // Called from code-behind after rename edit box closes
    public async Task RenameNode(string nodeId, string newName)
    {
        _treeService.Rename(nodeId, newName);
        await _treeService.SaveAsync();
        Rebuild();
    }

    // Called by WorkspaceService after a terminal is added to canvas
    public async Task RegisterTerminalAsync(string terminalTitle, string canvasItemId, string? parentId = null)
    {
        _treeService.AddTerminalNode(terminalTitle, canvasItemId, parentId);
        await _treeService.SaveAsync();
        Rebuild();
    }

    // Called by WorkspaceService after a canvas item is removed
    public async Task UnregisterCanvasItemAsync(string canvasItemId)
    {
        var node = _treeService.RootNodes
            .SelectMany(Flatten)
            .FirstOrDefault(n => n.LinkedCanvasItemId == canvasItemId);
        if (node is not null)
        {
            _treeService.Remove(node.Id);
            await _treeService.SaveAsync();
            Rebuild();
        }
    }

    // Returns true if a tree node already exists for the given canvas item id (duplicate guard)
    public bool HasNodeForCanvasItem(string canvasItemId)
        => _treeService.RootNodes.SelectMany(Flatten).Any(n => n.LinkedCanvasItemId == canvasItemId);

    // Syncs the tree node label when the terminal title changes (e.g. via OSC sequences)
    public async Task SyncTerminalTitleAsync(string canvasItemId, string newTitle)
    {
        var node = _treeService.RootNodes
            .SelectMany(Flatten)
            .FirstOrDefault(n => n.LinkedCanvasItemId == canvasItemId);
        if (node is null) return;
        _treeService.Rename(node.Id, newTitle);
        await _treeService.SaveAsync();
        Rebuild();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        RootNodes.Clear();
        foreach (var model in _treeService.RootNodes)
            RootNodes.Add(new WorkspaceTreeNodeViewModel(model));
    }

    private void SelectAndEdit(string nodeId)
    {
        var vm = FindNodeVm(nodeId);
        if (vm is null) return;
        SelectedNode = vm;
        vm.IsEditing = true;
    }

    private WorkspaceTreeNodeViewModel? FindNodeVm(string id)
        => RootNodes.SelectMany(Flatten).FirstOrDefault(n => n.Model.Id == id);

    private static System.Collections.Generic.IEnumerable<WorkspaceTreeNodeViewModel> Flatten(WorkspaceTreeNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private static System.Collections.Generic.IEnumerable<WorkspaceNodeModel> Flatten(WorkspaceNodeModel node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }
}
