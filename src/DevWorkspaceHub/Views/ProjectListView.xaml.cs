using System.Windows;
using System.Windows.Controls;

namespace DevWorkspaceHub.Views;

public partial class ProjectListView : UserControl
{
    public ProjectListView()
    {
        InitializeComponent();
    }

    private void OnProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is Models.Project project
            && listBox.DataContext is ViewModels.ProjectListViewModel vm)
        {
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
