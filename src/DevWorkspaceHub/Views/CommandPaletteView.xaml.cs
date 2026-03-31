using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Views;

/// <summary>
/// Code-behind for CommandPaletteView.
/// Handles auto-focus of the search box when the palette opens
/// and Escape key at the UserControl level.
/// </summary>
public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private CommandPaletteViewModel? ViewModel => DataContext as CommandPaletteViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && ViewModel is { IsOpen: true })
        {
            // Focus the search box when palette opens (dispatched to ensure visual tree is ready)
            Dispatcher.BeginInvoke(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Handle Escape at the UserControl level to close the palette.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ViewModel is not null)
            {
                ViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }
}
