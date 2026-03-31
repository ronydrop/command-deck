using System.Windows;
using System.Windows.Controls;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

public partial class BrowserView : UserControl
{
    private bool _initialized;

    public BrowserView()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        if (DataContext is BrowserViewModel vm)
        {
            vm.SetWebView(WebView);
            await vm.InitializeAsync();
        }
    }
}
