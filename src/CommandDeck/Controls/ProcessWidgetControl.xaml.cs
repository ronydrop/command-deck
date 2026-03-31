using System.Windows;
using System.Windows.Controls;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

public partial class ProcessWidgetControl : UserControl
{
    public ProcessWidgetControl()
    {
        InitializeComponent();
    }

    private async void OnKillProcessClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int pid
            && DataContext is WidgetCanvasItemViewModel vm)
        {
            await vm.KillProcessAsync(pid);
        }
    }
}
