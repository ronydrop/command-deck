using System.Windows;
using System.Windows.Input;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

/// <summary>
/// Code-behind for the tabbed terminal layout view.
/// Handles tab click-to-activate and wires the DataContext.
/// </summary>
public partial class TabbedTerminalView : System.Windows.Controls.UserControl
{
    public TabbedTerminalView()
    {
        InitializeComponent();
    }

    // ─── Tab click ───────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the terminal whose tab was clicked by updating
    /// <see cref="TerminalManagerViewModel.ActiveTerminal"/>.
    /// </summary>
    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var border = (System.Windows.Controls.Border)sender;
        if (border.DataContext is not TerminalViewModel clickedVm) return;

        if (DataContext is not TabbedTerminalViewModel vm) return;

        vm.TerminalManager.ActiveTerminal = clickedVm;
        e.Handled = true;
    }
}
