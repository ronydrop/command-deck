using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace CommandDeck.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() { InitializeComponent(); }

    private void OnLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
