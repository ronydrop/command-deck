using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommandDeck.Converters;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Code-behind for the Kanban board widget.
/// Handles drag-drop card movement, inline column management, card expand/collapse, and
/// all event routing from elements inside DataTemplates (which can't bind commands directly).
/// State lives in <see cref="WidgetCanvasItemViewModel"/>;
/// column/card transient state lives in <see cref="KanbanColumnViewModel"/> and
/// <see cref="KanbanCardViewModel"/>.
/// </summary>
public partial class KanbanBoardControl : UserControl
{
    // ── Drag-drop tracking ────────────────────────────────────────────────────

    private Point _dragStartPoint;
    private bool _isDragging;

    public KanbanBoardControl()
    {
        InitializeComponent();
    }

    // ── Board header buttons ──────────────────────────────────────────────────

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is WidgetCanvasItemViewModel vm)
            _ = vm.LoadKanbanBoardAsync();
    }

    private void OnAddColumnClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is WidgetCanvasItemViewModel vm)
            _ = AddColumnAndEditAsync(vm);
    }

    /// <summary>
    /// Adds a "Nova Lista" column and immediately activates its inline title editor.
    /// </summary>
    private async Task AddColumnAndEditAsync(WidgetCanvasItemViewModel vm)
    {
        await vm.AddColumnAsync("Nova Lista").ConfigureAwait(false);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var lastCol = vm.KanbanColumnViewModels.Count > 0
                ? vm.KanbanColumnViewModels[vm.KanbanColumnViewModels.Count - 1]
                : null;
            if (lastCol is not null)
                lastCol.IsEditingTitle = true;
        });
    }

    // ── Column header: rename ─────────────────────────────────────────────────

    private void OnColumnTitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        if (sender is FrameworkElement fe && fe.Tag is KanbanColumnViewModel colVm)
            colVm.IsEditingTitle = true;
        e.Handled = true;
    }

    private void OnColumnTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not KanbanColumnViewModel colVm) return;

        if (e.Key == Key.Enter)
        {
            colVm.CommitTitleEdit();
            if (DataContext is WidgetCanvasItemViewModel vm)
                _ = vm.UpdateColumnAsync(colVm.Column);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            colVm.CancelTitleEdit();
            e.Handled = true;
        }
    }

    /// <summary>Auto-focuses and selects-all when the column title TextBox becomes visible.</summary>
    private void OnColumnEditBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && sender is TextBox tb)
            tb.Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); });
    }

    // ── Column header: delete ─────────────────────────────────────────────────

    private void OnDeleteColumnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KanbanColumnViewModel colVm) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        if (vm.KanbanColumnViewModels.Count <= 1)
        {
            MessageBox.Show("Não é possível excluir a única coluna do board.",
                "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Excluir coluna \"{colVm.Title}\"?\nOs cards serão movidos para a primeira coluna.",
            "Confirmar exclusão",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
            _ = vm.DeleteColumnAsync(colVm.Id);

        e.Handled = true;
    }

    // ── Column header: add card ───────────────────────────────────────────────

    private void OnAddCardToColumnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KanbanColumnViewModel colVm) return;
        colVm.IsAddingCard = !colVm.IsAddingCard;
        if (!colVm.IsAddingCard)
            colVm.NewCardTitle = string.Empty;
        e.Handled = true;
    }

    /// <summary>Auto-focuses the add-card TextBox when it becomes visible.</summary>
    private void OnAddCardBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && sender is TextBox tb)
            tb.Dispatcher.BeginInvoke(() => tb.Focus());
    }

    private void OnColumnAddCardKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not KanbanColumnViewModel colVm) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            var title = colVm.NewCardTitle.Trim();
            if (!string.IsNullOrEmpty(title))
            {
                _ = vm.AddCardAsync(title, colVm.Id);
                colVm.NewCardTitle = string.Empty;
                colVm.IsAddingCard = false;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            colVm.NewCardTitle = string.Empty;
            colVm.IsAddingCard = false;
            e.Handled = true;
        }
    }

    private void OnCancelAddCard(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KanbanColumnViewModel colVm) return;
        colVm.NewCardTitle = string.Empty;
        colVm.IsAddingCard = false;
        e.Handled = true;
    }

    // ── Card buttons ──────────────────────────────────────────────────────────

    private void OnExecuteCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KanbanCardViewModel cardVm) return;
        if (DataContext is WidgetCanvasItemViewModel vm)
            _ = vm.ExecuteCardAsync(cardVm.Id);
        e.Handled = true;
    }

    private void OnToggleExpandCard(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KanbanCardViewModel cardVm) return;
        cardVm.IsExpanded = !cardVm.IsExpanded;
        e.Handled = true;
    }

    private void OnDeleteCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KanbanCardViewModel cardVm) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        var result = MessageBox.Show(
            $"Excluir card \"{cardVm.Title}\"?",
            "Confirmar exclusão",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
            _ = vm.DeleteCardAsync(cardVm.Id);

        e.Handled = true;
    }

    // ── Drag & Drop ───────────────────────────────────────────────────────────

    /// <summary>
    /// Records the starting point when the user presses the left mouse button on a card item.
    /// Must be wired via EventSetter on <see cref="KanbanCardItemStyle"/>.
    /// </summary>
    private void OnCardMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    /// <summary>
    /// Initiates a drag operation once the pointer has moved beyond the OS drag threshold.
    /// Payload is the <see cref="KanbanCardViewModel"/> of the dragged card.
    /// Must be wired via EventSetter on <see cref="KanbanCardItemStyle"/>.
    /// </summary>
    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;

        var pos = e.GetPosition(null);
        var dx = Math.Abs(pos.X - _dragStartPoint.X);
        var dy = Math.Abs(pos.Y - _dragStartPoint.Y);

        if (dx < SystemParameters.MinimumHorizontalDragDistance &&
            dy < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is ListBoxItem item && item.DataContext is KanbanCardViewModel cardVm)
        {
            _isDragging = true;
            DragDrop.DoDragDrop(item, cardVm, DragDropEffects.Move);
            _isDragging = false;
        }
    }

    /// <summary>Highlights the target column and accepts the drag operation.</summary>
    private void OnColumnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(KanbanCardViewModel))) return;
        SetColumnDragOver(sender, true);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>Keeps the move cursor active while hovering over a column.</summary>
    private void OnColumnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(KanbanCardViewModel))) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>Removes the highlight when the drag leaves a column.</summary>
    private void OnColumnDragLeave(object sender, DragEventArgs e)
    {
        SetColumnDragOver(sender, false);
    }

    /// <summary>
    /// Moves the dragged card to the target column.
    /// Gets the column VM from the ListBox's DataContext (which is the KanbanColumnViewModel
    /// bound by the column DataTemplate).
    /// </summary>
    private void OnColumnDrop(object sender, DragEventArgs e)
    {
        SetColumnDragOver(sender, false);

        if (e.Data.GetData(typeof(KanbanCardViewModel)) is not KanbanCardViewModel dragCard) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        var targetCol = GetColumnVm(sender);
        if (targetCol is null) return;

        // Don't no-op same-column drops (future: could trigger reorder)
        if (dragCard.Card.ColumnId != targetCol.Id)
            _ = vm.MoveCardToColumnAsync(dragCard.Card.Id, targetCol.Id);

        e.Handled = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static KanbanColumnViewModel? GetColumnVm(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.DataContext is KanbanColumnViewModel colVm) return colVm;
            if (fe.Tag is KanbanColumnViewModel tagVm) return tagVm;
        }
        return null;
    }

    private static void SetColumnDragOver(object sender, bool value)
    {
        var colVm = GetColumnVm(sender);
        if (colVm is not null)
            colVm.IsDragOver = value;
    }

    // ── Card editor handlers ──────────────────────────────────────────────────

    private void OnCardTabClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is KanbanCardViewModel cardVm && fe.Tag is string tab)
            cardVm.ActiveTab = tab;
        e.Handled = true;
    }

    private void OnCardEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not KanbanCardViewModel cardVm) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter) ||
            (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S))
        {
            _ = vm.SaveCardAsync(cardVm.CommitEdit());
            cardVm.IsExpanded = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            cardVm.BeginEdit();  // revert
            cardVm.IsExpanded = false;
            e.Handled = true;
        }
    }

    private void OnSaveCardEdit(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KanbanCardViewModel cardVm) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;
        _ = vm.SaveCardAsync(cardVm.CommitEdit());
        cardVm.IsExpanded = false;
        e.Handled = true;
    }

    private void OnCancelCardEdit(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KanbanCardViewModel cardVm) return;
        cardVm.BeginEdit();  // revert drafts
        cardVm.IsExpanded = false;
        e.Handled = true;
    }

    private void OnAgentPillClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is KanbanCardViewModel cardVm && fe.Tag is string agent)
        {
            cardVm.DraftAgent = agent;
            // Reset model to first for this agent
            var models = new AgentToModelsConverter().Convert(agent, typeof(string[]), null, System.Globalization.CultureInfo.InvariantCulture) as string[];
            cardVm.DraftModel = models?.Length > 0 ? models[0] : string.Empty;
        }
        e.Handled = true;
    }

    private void OnModelPillClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is string model)
        {
            // Walk up to find card VM — the DataContext inside ItemsControl item is the string
            var parent = VisualTreeHelper.GetParent(fe);
            while (parent is not null)
            {
                if (parent is FrameworkElement parentFe && parentFe.DataContext is KanbanCardViewModel cardVm)
                {
                    cardVm.DraftModel = model;
                    break;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
        }
        e.Handled = true;
    }

    private void OnCardColorPickerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KanbanCardViewModel cardVm) return;

        var popup = new ColorPickerPopup
        {
            PlacementTarget = fe,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        popup.ColorSelected += hex =>
        {
            cardVm.DraftColor = hex ?? string.Empty;
            // Also update immediately so the accent stripe updates
            cardVm.Card.Color = hex ?? string.Empty;
            cardVm.OnPropertyChanged(nameof(KanbanCardViewModel.Color));
        };
        popup.IsOpen = true;
        e.Handled = true;
    }

    private void OnAddCommentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KanbanCardViewModel cardVm) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        // Find the TextBox for the comment in the visual tree
        var parent = VisualTreeHelper.GetParent(fe);
        TextBox? commentBox = null;
        while (parent is not null)
        {
            if (parent is FrameworkElement pfe)
            {
                commentBox = FindChild<TextBox>(pfe, "NewCommentBox");
                if (commentBox is not null) break;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }

        var text = commentBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        _ = vm.AddCommentAsync(cardVm.Id, text);
        if (commentBox is not null) commentBox.Text = string.Empty;
        e.Handled = true;
    }

    private void OnNewCommentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is not TextBox tb || tb.Tag is not KanbanCardViewModel cardVm) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        var text = tb.Text.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            _ = vm.AddCommentAsync(cardVm.Id, text);
            tb.Text = string.Empty;
        }
        e.Handled = true;
    }

    private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name) return fe;
            var result = FindChild<T>(child, name);
            if (result is not null) return result;
        }
        return null;
    }

    // ── Card title inline rename ──────────────────────────────────────────────

    private void OnCardTitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        if (sender is FrameworkElement fe && fe.Tag is KanbanCardViewModel cardVm)
        {
            cardVm.DraftTitle = cardVm.Title;
            cardVm.IsEditingTitle = true;
        }
        e.Handled = true;
    }

    private void OnCardTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not KanbanCardViewModel cardVm) return;
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            var trimmed = cardVm.DraftTitle.Trim();
            if (trimmed.Length > 0)
            {
                cardVm.Card.Title = trimmed;
                cardVm.OnPropertyChanged(nameof(KanbanCardViewModel.Title));
                _ = vm.SaveCardAsync(cardVm.Card);
            }
            cardVm.IsEditingTitle = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            cardVm.DraftTitle = cardVm.Title;
            cardVm.IsEditingTitle = false;
            e.Handled = true;
        }
    }

    /// <summary>Auto-focuses and selects-all when the card title TextBox becomes visible.</summary>
    private void OnCardTitleEditBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && sender is TextBox tb)
            tb.Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); });
    }
}
