using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Views;

/// <summary>
/// Code-behind for the AI Assistant side panel.
/// Auto-scrolls chat and handles Enter/Shift+Enter for the input box.
/// </summary>
public partial class AssistantPanelView : UserControl
{
    public AssistantPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is AssistantPanelViewModel vm)
        {
            _ = vm.OnPanelOpenedAsync();

            // Focus the input on open
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                ChatInput?.Focus();
            });
        }
        else if (e.NewValue is false && DataContext is AssistantPanelViewModel closedVm)
        {
            closedVm.OnPanelClosed();
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            ChatScrollViewer?.ScrollToEnd();
        });
    }

    /// <summary>
    /// Override to handle Enter (send) vs Shift+Enter (new line) in the input TextBox.
    /// The KeyBinding in XAML handles basic Enter, but we need to suppress the newline
    /// character that AcceptsReturn=True would insert on plain Enter.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ChatInput is not null && ChatInput.IsFocused)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Shift+Enter: insert newline (let it through)
                return;
            }

            // Plain Enter: send the message, suppress the newline
            if (DataContext is AssistantPanelViewModel vm && vm.SendMessageCommand.CanExecute(null))
            {
                vm.SendMessageCommand.Execute(null);
            }
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }
}
