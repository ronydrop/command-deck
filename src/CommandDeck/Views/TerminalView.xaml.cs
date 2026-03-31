using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

public partial class TerminalView : UserControl
{
    public TerminalView()
    {
        InitializeComponent();
    }

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is TerminalViewModel terminal)
        {
            var mainVm = (DataContext as FrameworkElement)?.DataContext as MainViewModel;
            if (mainVm == null)
            {
                // Navigate up the visual tree to find the MainViewModel
                var window = Window.GetWindow(this);
                mainVm = window?.DataContext as MainViewModel;
            }

            if (mainVm != null)
            {
                mainVm.ActiveTerminal = terminal;
            }
        }
    }
}
