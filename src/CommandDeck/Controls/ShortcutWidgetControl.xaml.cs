using System.Collections.Specialized;
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
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is WidgetCanvasItemViewModel oldVm)
            oldVm.Shortcuts.CollectionChanged -= OnShortcutsChanged;

        if (e.NewValue is WidgetCanvasItemViewModel newVm)
        {
            newVm.Shortcuts.CollectionChanged += OnShortcutsChanged;
            UpdateEmptyState(newVm);
        }
    }

    private void OnShortcutsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is WidgetCanvasItemViewModel vm)
            UpdateEmptyState(vm);
    }

    private void UpdateEmptyState(WidgetCanvasItemViewModel vm)
    {
        EmptyLabel.Visibility = vm.Shortcuts.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
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
