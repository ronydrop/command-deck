using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            try
            {
                await vm.ExecuteShortcutAsync(command);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShortcutWidget] Execute failed: {ex}");
            }
        }
    }

    private void OnAddShortcut(object sender, RoutedEventArgs e) => AddNewShortcut();

    private void OnNewCommandKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddNewShortcut();
    }

    private void AddNewShortcut()
    {
        var cmd = NewCommandInput.Text?.Trim();
        if (string.IsNullOrEmpty(cmd)) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        vm.AddShortcut(cmd);
        NewCommandInput.Clear();
        NewCommandInput.Focus();
    }

    private void OnRemoveShortcut(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string command
            && DataContext is WidgetCanvasItemViewModel vm)
        {
            vm.RemoveShortcut(command);
        }
    }
}
