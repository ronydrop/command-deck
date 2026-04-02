using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the workspace hierarchy sidebar.
/// Owns the flat + tree display of WorkspaceNodeModels (groups and projects).
/// </summary>
public partial class WorkspaceTreeViewModel : ObservableObject
{
    private readonly IWorkspaceTreeService _treeService;

    public ObservableCollection<WorkspaceTreeNodeViewModel> RootNodes { get; } = new();

    [ObservableProperty] private WorkspaceTreeNodeViewModel? _selectedNode;

    public WorkspaceTreeViewModel(IWorkspaceTreeService treeService)
    {
        _treeService = treeService;
    }

    public async Task LoadAsync()
    {
        await _treeService.LoadAsync();
        Rebuild();
    }

    /// <summary>
    /// Validates the tree against the current set of canvas item IDs,
    /// removing orphan terminal nodes that no longer have a matching canvas item.
    /// </summary>
    public async Task ValidateAgainstCanvasAsync(IReadOnlySet<string> validCanvasItemIds)
    {
        var removed = await _treeService.ValidateAndCleanAsync(validCanvasItemIds);
        if (removed > 0) Rebuild();
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
        SelectedNode.Refresh(); // Incremental: just refresh the changed node
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task AddGroup()
    {
        try
        {
            var node = _treeService.AddGroup("Novo Grupo");
            await _treeService.SaveAsync();
            var vm = new WorkspaceTreeNodeViewModel(node);
            EnsureUiThread(() => RootNodes.Add(vm));
            // Auto-select the new node and enter edit mode
            SelectedNode = vm;
            vm.IsEditing = true;
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
            var toRemove = SelectedNode;
            _treeService.Remove(toRemove.Model.Id);
            await _treeService.SaveAsync();
            RemoveNodeFromTree(toRemove);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceTree] DeleteSelected failed: {ex}");
        }
    }

    [RelayCommand]
    private void ActivateSelected()
    {
        // Reserved for future use (e.g. pan canvas to a linked item)
    }

    // Called from code-behind after rename edit box closes
    public async Task RenameNode(string nodeId, string newName)
    {
        _treeService.Rename(nodeId, newName);
        await _treeService.SaveAsync();
        FindNodeVm(nodeId)?.Refresh(); // Incremental: just refresh the renamed node
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        EnsureUiThread(() =>
        {
            RootNodes.Clear();
            foreach (var model in _treeService.RootNodes)
                RootNodes.Add(new WorkspaceTreeNodeViewModel(model));
        });
    }

    /// <summary>
    /// Removes a node VM from the tree, checking both root and parent collections.
    /// </summary>
    private void RemoveNodeFromTree(WorkspaceTreeNodeViewModel nodeVm)
    {
        // Try to find a parent that contains this node
        var parentVm = RootNodes.SelectMany(Flatten)
            .FirstOrDefault(n => n.Children.Contains(nodeVm));

        EnsureUiThread(() =>
        {
            if (parentVm is not null)
                parentVm.RemoveChild(nodeVm);
            else
                RootNodes.Remove(nodeVm);

            if (SelectedNode == nodeVm)
                SelectedNode = null;
        });
    }

    /// <summary>Ensures the action runs on the WPF UI thread.</summary>
    private static void EnsureUiThread(Action action)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            dispatcher.Invoke(action);
        else
            action();
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
}
