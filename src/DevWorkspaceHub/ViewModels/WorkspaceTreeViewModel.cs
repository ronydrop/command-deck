using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel for the workspace hierarchy sidebar.
/// Owns the flat + tree display of WorkspaceNodeModels.
/// Manages the workspace selector (multi-workspace support).
/// Raises NodeActivated so the View can pan the camera to the linked canvas item.
/// </summary>
public partial class WorkspaceTreeViewModel : ObservableObject
{
    private readonly IWorkspaceTreeService _treeService;
    private readonly IWorkspaceService _workspaceService;

    public ObservableCollection<WorkspaceTreeNodeViewModel> RootNodes { get; } = new();

    /// <summary>All available workspaces for the selector dropdown.</summary>
    public ObservableCollection<WorkspaceModel> Workspaces { get; } = new();

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<WorkspaceTreeNodeViewModel> _visibleNodes = new();
    [ObservableProperty] private WorkspaceTreeNodeViewModel? _selectedNode;
    [ObservableProperty] private WorkspaceModel? _activeWorkspace;
    [ObservableProperty] private bool _isWorkspaceSelectorOpen;

    /// <summary>Raised when the user activates a node — canvas should center on it.</summary>
    public event Action<string>? NodeActivated; // payload: canvasItemId

    /// <summary>Raised when the user switches to a different workspace.</summary>
    public event Action<string>? WorkspaceSwitchRequested; // payload: workspaceId

    public WorkspaceTreeViewModel(IWorkspaceTreeService treeService, IWorkspaceService workspaceService)
    {
        _treeService = treeService;
        _workspaceService = workspaceService;

        _workspaceService.ActiveWorkspaceChanged += OnWorkspaceServiceActiveChanged;
    }

    public async Task LoadAsync()
    {
        await _treeService.LoadAsync();
        await RefreshWorkspaceListAsync();
        Rebuild();
    }

    private async void OnWorkspaceServiceActiveChanged(WorkspaceModel workspace)
    {
        ActiveWorkspace = workspace;
        SearchQuery = string.Empty;
        await _treeService.LoadAsync();
        Rebuild();
        await RefreshWorkspaceListAsync();
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddGroup()
    {
        var node = _treeService.AddGroup("Novo Grupo");
        await _treeService.SaveAsync();
        Rebuild();
        // Auto-select the new node and enter edit mode
        SelectAndEdit(node.Id);
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        if (SelectedNode is null) return;
        _treeService.Remove(SelectedNode.Model.Id);
        await _treeService.SaveAsync();
        Rebuild();
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

    // ─── Workspace Management ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task CreateWorkspace()
    {
        var ws = await _workspaceService.CreateWorkspaceAsync("Novo Workspace");
        await RefreshWorkspaceListAsync();
    }

    [RelayCommand]
    private void SwitchWorkspace(WorkspaceModel? workspace)
    {
        if (workspace is null || workspace.Id == ActiveWorkspace?.Id) return;
        IsWorkspaceSelectorOpen = false;
        WorkspaceSwitchRequested?.Invoke(workspace.Id);
    }

    [RelayCommand]
    private async Task DeleteWorkspace(WorkspaceModel? workspace)
    {
        if (workspace is null || workspace.Id == ActiveWorkspace?.Id) return;
        await _workspaceService.DeleteWorkspaceAsync(workspace.Id);
        await RefreshWorkspaceListAsync();
    }

    [RelayCommand]
    private void ToggleWorkspaceSelector()
    {
        IsWorkspaceSelectorOpen = !IsWorkspaceSelectorOpen;
    }

    public async Task RenameWorkspace(string workspaceId, string newName)
    {
        await _workspaceService.RenameWorkspaceAsync(workspaceId, newName);
        await RefreshWorkspaceListAsync();
    }

    public async Task UpdateWorkspaceColor(string workspaceId, string newColor)
    {
        await _workspaceService.UpdateWorkspaceColorAsync(workspaceId, newColor);
        await RefreshWorkspaceListAsync();
    }

    private async Task RefreshWorkspaceListAsync()
    {
        var list = await _workspaceService.ListWorkspacesAsync();
        Workspaces.Clear();
        foreach (var ws in list)
            Workspaces.Add(ws);

        ActiveWorkspace = _workspaceService.CurrentWorkspace;
    }

    // ─── Search ───────────────────────────────────────────────────────────────

    partial void OnSearchQueryChanged(string value) => ApplySearch(value);

    private void ApplySearch(string query)
    {
        VisibleNodes.Clear();
        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var n in RootNodes) VisibleNodes.Add(n);
            return;
        }
        var matches = _treeService.Search(query);
        foreach (var m in matches)
        {
            var vm = FindNodeVm(m.Id);
            if (vm is not null) VisibleNodes.Add(vm);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        RootNodes.Clear();
        foreach (var model in _treeService.RootNodes)
            RootNodes.Add(new WorkspaceTreeNodeViewModel(model));

        ApplySearch(SearchQuery);
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
