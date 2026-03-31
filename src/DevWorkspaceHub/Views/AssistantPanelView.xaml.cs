using System.Collections.Specialized;
using System.Windows.Controls;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Views;

/// <summary>
/// Code-behind for the AI Assistant side panel.
/// Keeps logic minimal — only auto-scrolls the chat when new messages arrive.
/// </summary>
public partial class AssistantPanelView : UserControl
{
    public AssistantPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is AssistantPanelViewModel vm)
        {
            vm.Messages.CollectionChanged += OnMessagesChanged;
        }
        if (e.OldValue is AssistantPanelViewModel oldVm)
        {
            oldVm.Messages.CollectionChanged -= OnMessagesChanged;
        }
    }

    private void OnIsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        // Refresh provider availability when the panel becomes visible
        if (e.NewValue is true && DataContext is AssistantPanelViewModel vm)
        {
            _ = vm.OnPanelOpenedAsync();
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Auto-scroll to bottom when new messages are added
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            ChatScrollViewer?.ScrollToEnd();
        });
    }
}
