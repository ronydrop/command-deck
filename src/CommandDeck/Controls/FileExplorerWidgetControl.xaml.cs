using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

public partial class FileExplorerWidgetControl : UserControl
{
    private FileExplorerCanvasItemViewModel? _vm;

    public FileExplorerWidgetControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = e.NewValue as FileExplorerCanvasItemViewModel;
    }

    private void OnNodeClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null) return;

        var node = (sender as FrameworkElement)?.DataContext as FileTreeNode;
        if (node is null) return;

        if (node.IsDirectory)
        {
            // Toggle expand on single click
            _vm.ExpandNodeCommand.Execute(node);
        }
        else if (e.ClickCount >= 2)
        {
            // Open file on double-click
            _vm.OpenFileCommand.Execute(node);
        }
    }

    private void OnTreeItemExpanded(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var item = e.OriginalSource as TreeViewItem;
        var node = item?.DataContext as FileTreeNode;
        if (node is null || !node.IsDirectory) return;

        // Load children lazily
        _ = _vm.ExpandNodeCommand.ExecuteAsync(node);
    }

    private void OnSearchItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null) return;
        var item = SearchList.SelectedItem as FileTreeNode;
        if (item is null) return;
        _vm.OpenFileCommand.Execute(item);
    }
}
