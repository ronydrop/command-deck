using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

public partial class CommandPaletteOverlay : UserControl
{
    private CommandPaletteViewModel? _vm;

    public CommandPaletteOverlay()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as CommandPaletteViewModel;
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                SearchBox.Clear();
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
            }
        };
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _vm?.CloseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                _vm?.ConfirmCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                _vm?.MoveDownCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                _vm?.MoveUpCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnDimLayerClick(object sender, MouseButtonEventArgs e)
    {
        _vm?.CloseCommand.Execute(null);
    }

    private void OnResultClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CommandDefinitionModel cmd)
        {
            if (_vm is not null) _vm.SelectedResult = cmd;
            _vm?.ConfirmCommand.Execute(null);
        }
    }
}
