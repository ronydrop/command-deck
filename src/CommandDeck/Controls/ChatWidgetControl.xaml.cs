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
/// Code-behind for the AI Chat widget control.
/// Handles input (Ctrl+Enter to send), auto-scroll, and clear.
/// Logic lives in <see cref="WidgetCanvasItemViewModel"/>.
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
        if (DataContext is WidgetCanvasItemViewModel vm)
            vm.ChatMessages.CollectionChanged += OnMessagesChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is WidgetCanvasItemViewModel vm)
            vm.ChatMessages.CollectionChanged -= OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Auto-scroll to bottom whenever a new message is added
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToBottom());
    }

    private void OnChatInputKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+Enter sends the message
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SendMessage();
            e.Handled = true;
        }
    }

    private void OnSendClicked(object sender, RoutedEventArgs e) => SendMessage();

    private void SendMessage()
    {
        if (DataContext is not WidgetCanvasItemViewModel vm) return;
        _ = vm.SendChatMessageAsync();
        ChatInput.Focus();
    }

    private void OnClearMessages(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WidgetCanvasItemViewModel vm) return;
        vm.ChatMessages.Clear();
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

        // Special mode: return inverted bool for IsEnabled bindings
        if (parameter is string p && p == "BoolToEnabled")
            return !boolValue;

        return boolValue ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
