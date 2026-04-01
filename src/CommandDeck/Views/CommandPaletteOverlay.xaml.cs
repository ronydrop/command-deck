using System.Windows.Controls;
using System.Windows.Input;
using CommandDeck.Controls;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

public partial class CommandPaletteOverlay : SearchOverlayControlBase
{
    private CommandPaletteViewModel? _vm;

    public CommandPaletteOverlay()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as CommandPaletteViewModel;
    }

    private void OnResultClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is CommandDefinitionModel cmd)
        {
            if (_vm is not null) _vm.SelectedResult = cmd;
            _vm?.ConfirmCommand.Execute(null);
        }
    }
}
