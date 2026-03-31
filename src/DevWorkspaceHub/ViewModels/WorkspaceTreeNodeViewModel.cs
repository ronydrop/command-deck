using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel for a single node in the workspace tree sidebar.
/// Wraps WorkspaceNodeModel with observable expansion/selection state.
/// </summary>
public partial class WorkspaceTreeNodeViewModel : ObservableObject
{
    public WorkspaceNodeModel Model { get; }

    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editingName = string.Empty;

    partial void OnIsEditingChanged(bool value)
    {
        if (value) EditingName = Model.Name;
    }

    public ObservableCollection<WorkspaceTreeNodeViewModel> Children { get; } = new();

    // Forwarded from model for direct XAML binding
    public string Name => Model.Name;
    public string Color => Model.Color;
    public string IconKey => Model.IconKey;
    public WorkspaceNodeType NodeType => Model.NodeType;

    public WorkspaceTreeNodeViewModel(WorkspaceNodeModel model)
    {
        Model = model;
        foreach (var child in model.Children)
            Children.Add(new WorkspaceTreeNodeViewModel(child));
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    public void AddChild(WorkspaceTreeNodeViewModel child)
    {
        Children.Add(child);
        Model.Children.Add(child.Model);
    }

    public void RemoveChild(WorkspaceTreeNodeViewModel child)
    {
        Children.Remove(child);
        Model.Children.Remove(child.Model);
    }
}
