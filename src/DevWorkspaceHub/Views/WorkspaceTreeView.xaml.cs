using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Views;

/// <summary>
/// Code-behind for the workspace hierarchy sidebar tree-view.
/// Handles selection and double-click activation for both the hierarchical
/// TreeView (shown when SearchQuery is empty) and the flat search ListBox.
/// </summary>
public partial class WorkspaceTreeView : UserControl
{
    public WorkspaceTreeView()
    {
        InitializeComponent();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private WorkspaceTreeViewModel? Vm => DataContext as WorkspaceTreeViewModel;

    // ─── Hierarchical TreeView handlers ───────────────────────────────────────

    /// <summary>
    /// Propagates the WPF TreeView selection to the ViewModel so that
    /// SelectedNode is kept in sync when the user clicks a node.
    /// </summary>
    private void HierarchyTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Vm is null) return;
        if (e.NewValue is WorkspaceTreeNodeViewModel node)
            Vm.SelectedNode = node;
    }

    /// <summary>
    /// Double-click on the hierarchical tree fires ActivateSelectedCommand.
    /// We filter to left-button to avoid spurious triggers on expand chevrons.
    /// </summary>
    private void HierarchyTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (Vm?.ActivateSelectedCommand.CanExecute(null) == true)
            Vm.ActivateSelectedCommand.Execute(null);
    }

    // ─── Flat search ListBox handlers ─────────────────────────────────────────

    /// <summary>
    /// Double-click in the flat search results fires ActivateSelectedCommand.
    /// SelectedNode is already kept in sync via TwoWay binding on SelectedItem.
    /// </summary>
    private void SearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (Vm?.ActivateSelectedCommand.CanExecute(null) == true)
            Vm.ActivateSelectedCommand.Execute(null);
    }

    // ─── Node inline rename handlers ─────────────────────────────────────────

    private void NodeRenameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not WorkspaceTreeNodeViewModel node) return;
        if (e.Key == Key.Return)
        {
            e.Handled = true;
            Vm?.CommitRenameNodeCommand.Execute(node);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            node.IsEditing = false;
        }
    }

    private void NodeRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is WorkspaceTreeNodeViewModel node && node.IsEditing)
            Vm?.CommitRenameNodeCommand.Execute(node);
    }

    private void NodeRenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    // ─── Workspace inline rename handlers ────────────────────────────────────

    private void WorkspaceRenameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            e.Handled = true;
            Vm?.CommitRenameWorkspaceCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (Vm is not null) Vm.EditingWorkspace = null;
        }
    }

    private void WorkspaceRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (Vm?.EditingWorkspace is not null)
            Vm.CommitRenameWorkspaceCommand.Execute(null);
    }

    private void WorkspaceRenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }
}
