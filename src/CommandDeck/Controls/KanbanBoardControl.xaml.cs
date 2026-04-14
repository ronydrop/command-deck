using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Code-behind for the Kanban board widget control.
/// Handles input for adding new cards and refresh button.
/// All board state lives in <see cref="WidgetCanvasItemViewModel"/>.
/// </summary>
public partial class KanbanBoardControl : UserControl
{
    public KanbanBoardControl()
    {
        InitializeComponent();
    }

    private void OnAddCardKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddNewCard();
        e.Handled = true;
    }

    private void OnAddCardButtonClick(object sender, RoutedEventArgs e)
    {
        AddNewCard();
    }

    private void AddNewCard()
    {
        var text = NewCardInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        _ = vm.AddCardAsync(text, "backlog");
        NewCardInput.Clear();
        NewCardInput.Focus();
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WidgetCanvasItemViewModel vm) return;
        _ = vm.LoadKanbanBoardAsync();
    }

    private void OnExecuteCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not KanbanCard card) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;
        _ = vm.ExecuteCardAsync(card.Id);
        e.Handled = true;
    }
}

/// <summary>
/// Converts a card's <c>ColumnId</c> string to <see cref="Visibility"/>.
/// ConverterParameter is the expected column id (e.g. "backlog").
/// Returns <see cref="Visibility.Visible"/> when they match, <see cref="Visibility.Collapsed"/> otherwise.
/// </summary>
public class ColumnIdToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string columnId && parameter is string expected)
            return columnId.Equals(expected, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
