using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CommandDeck.Views;

public partial class ProjectListView : UserControl
{
    private string? _lastOpenedProjectId;
    private Point _dragStartPoint;
    private bool _isDragging;

    public ProjectListView()
    {
        InitializeComponent();
    }

    private void OnActiveProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        // Clear selection on the inactive list to keep a single selection across both
        InactiveListBox.SelectedItem = null;
        OnProjectSelectionChanged(sender, e);
    }

    private void OnInactiveProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        // Clear selection on the active list to keep a single selection across both
        ActiveListBox.SelectedItem = null;
        OnProjectSelectionChanged(sender, e);
    }

    private void OnProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (sender is ListBox listBox && listBox.SelectedItem is Models.Project project
            && listBox.DataContext is ViewModels.ProjectListViewModel vm
            && _lastOpenedProjectId != project.Id)
        {
            _lastOpenedProjectId = project.Id;
            if (vm.OpenProjectCommand.CanExecute(project))
                vm.OpenProjectCommand.Execute(project);
        }
    }

    private void OnContextMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void OnListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is not ListBox listBox) return;
        var item = GetListBoxItemAt(listBox, e.GetPosition(listBox));
        if (item?.DataContext is not Models.Project project) return;

        _isDragging = true;
        DragDrop.DoDragDrop(item, project, DragDropEffects.Move);
        _isDragging = false;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(Models.Project))) return;
        if (sender is not ListBox listBox) return;
        if (listBox.DataContext is not ViewModels.ProjectListViewModel vm) return;

        var dragged = (Models.Project)e.Data.GetData(typeof(Models.Project));
        var targetItem = GetListBoxItemAt(listBox, e.GetPosition(listBox));
        if (targetItem?.DataContext is not Models.Project target) return;
        if (dragged.Id == target.Id) return;

        _ = vm.MoveProjectAsync(dragged, target);
    }

    private static ListBoxItem? GetListBoxItemAt(ListBox listBox, Point position)
    {
        var element = listBox.InputHitTest(position) as UIElement;
        while (element != null)
        {
            if (element is ListBoxItem item) return item;
            element = VisualTreeHelper.GetParent(element) as UIElement;
        }
        return null;
    }
}
