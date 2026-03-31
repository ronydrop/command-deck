using System.Windows;
using System.Windows.Controls;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Controls;

public partial class ShortcutWidgetControl : UserControl
{
    public ShortcutWidgetControl()
    {
        InitializeComponent();
    }

    private async void OnShortcutClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string command
            && DataContext is WidgetCanvasItemViewModel vm)
        {
            await vm.ExecuteShortcutAsync(command);
        }
    }
}
