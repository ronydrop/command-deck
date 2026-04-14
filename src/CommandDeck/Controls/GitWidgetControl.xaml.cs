using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Code-behind for the Git widget control.
/// Handles stage/unstage checkbox clicks and the stash section toggle.
/// All business logic is delegated to <see cref="WidgetCanvasItemViewModel"/>.
/// </summary>
public partial class GitWidgetControl : UserControl
{
    public GitWidgetControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is WidgetCanvasItemViewModel vm)
            _ = vm.RefreshGitChangedFilesAsync();
    }

    /// <summary>
    /// Fired when the user clicks a staged CheckBox in the file list.
    /// Delegates to the ViewModel to run git stage/unstage and refresh.
    /// </summary>
    private void OnStageCheckboxClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb) return;
        if (cb.Tag is not GitFileChangeViewModel fileVm) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        // Prevent double-fire from binding update
        e.Handled = true;

        _ = vm.ToggleStageFileAsync(fileVm);
    }

    /// <summary>
    /// Toggles the stash section expanded/collapsed state.
    /// </summary>
    private void OnStashHeaderClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WidgetCanvasItemViewModel vm) return;
        vm.StashSectionExpanded = !vm.StashSectionExpanded;
    }
}
