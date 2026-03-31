using System.Windows;
using System.Windows.Controls;

namespace CommandDeck.Views;

public partial class ProjectListView : UserControl
{
    private string? _lastOpenedProjectId;

    public ProjectListView()
    {
        InitializeComponent();
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
}
