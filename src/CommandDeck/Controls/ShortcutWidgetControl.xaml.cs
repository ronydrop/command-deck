using System.Windows;
using System.Windows.Controls;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

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
