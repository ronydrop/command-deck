using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Code-behind for the AI Chat tile widget.
/// Handles input (Ctrl+Enter to send), auto-scroll, clear, and copy.
/// Logic lives in <see cref="ChatCanvasItemViewModel"/>.
/// </summary>
public partial class ChatWidgetControl : UserControl
{
    public ChatWidgetControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatCanvasItemViewModel vm)
            vm.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChatCanvasItemViewModel vm)
            vm.Messages.CollectionChanged -= OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToBottom());
    }

    private void OnChatInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SendMessage();
            e.Handled = true;
        }
    }

    private void OnSendClicked(object sender, RoutedEventArgs e) => SendMessage();

    private void SendMessage()
    {
        if (DataContext is not ChatCanvasItemViewModel vm) return;
        _ = vm.SendMessageCommand.ExecuteAsync(null);
        ChatInput.Focus();
    }

    private void OnClearMessages(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChatCanvasItemViewModel vm) return;
        vm.ClearHistoryCommand.Execute(null);
    }

    private void OnCopyMessage(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ChatMessage msg)
        {
            try { Clipboard.SetText(msg.Content); }
            catch { /* clipboard may not be available */ }
        }
    }

    private void OnRetryMessage(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChatCanvasItemViewModel vm) return;
        _ = vm.RetryLastMessageCommand.ExecuteAsync(null);
    }

    private void OnProviderSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string provider
            && DataContext is ChatCanvasItemViewModel vm)
        {
            var providerType = provider switch
            {
                "Claude" => AssistantProviderType.Anthropic,
                "OpenAI" => AssistantProviderType.OpenAI,
                "OpenRouter" => AssistantProviderType.OpenRouter,
                "Local (Ollama)" => AssistantProviderType.Ollama,
                _ => AssistantProviderType.None
            };
            if (providerType != AssistantProviderType.None)
                vm.SwitchProviderCommand.Execute(providerType);
        }
    }

    private void OnAgentModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ChatCanvasItemViewModel vm &&
            sender is ComboBox cb && cb.SelectedItem is Models.AgentMode mode)
        {
            vm.SetAgentModeCommand.Execute(mode);
        }
    }

    private void OnTemplatePickerClick(object sender, RoutedEventArgs e)
    {
        var service = App.Services.GetService(typeof(Services.IPromptTemplateService)) as Services.IPromptTemplateService;
        if (service is null) return;

        TemplateList.ItemsSource = service.Templates;
        TemplatePickerPopup.IsOpen = !TemplatePickerPopup.IsOpen;
    }

    private void OnTemplateItemClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not Models.PromptTemplate template) return;
        TemplatePickerPopup.IsOpen = false;

        // If template has no fields, inject immediately
        if (template.Fields.Count == 0)
        {
            if (DataContext is ChatCanvasItemViewModel vm)
                vm.InputText = template.Template;
            return;
        }

        // Show field dialog for templates with dynamic fields
        var dialog = new TemplateFieldsDialog(template);
        if (dialog.ShowDialog() == true)
        {
            var rendered = template.Render(dialog.FieldValues);
            if (DataContext is ChatCanvasItemViewModel vm)
            {
                vm.InputText = rendered;
                if (template.AutoSend)
                    _ = vm.SendMessageCommand.ExecuteAsync(null);
            }
        }
    }
}

/// <summary>
/// Inverts a <see cref="bool"/> to <see cref="Visibility"/> (true → Collapsed, false → Visible).
/// When ConverterParameter is "BoolToEnabled", converts bool → bool (inverted) for IsEnabled bindings.
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        if (parameter is string p && p == "BoolToEnabled")
            return !boolValue;
        return boolValue ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
