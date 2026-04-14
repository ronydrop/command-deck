using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommandDeck.Models;

namespace CommandDeck.ViewModels;

/// <summary>
/// Presentation wrapper for a <see cref="KanbanColumn"/> that owns the per-column
/// card collection and transient drag-over / inline-edit state.
/// </summary>
public partial class KanbanColumnViewModel : ObservableObject
{
    /// <summary>The underlying column data model.</summary>
    public KanbanColumn Column { get; }

    /// <summary>Cards currently in this column, ordered by SortOrder.</summary>
    public ObservableCollection<KanbanCardViewModel> Cards { get; } = new();

    /// <summary>True while a card drag is hovering over this column (for visual drop feedback).</summary>
    [ObservableProperty] private bool _isDragOver;

    /// <summary>True while the column title is being edited inline via double-click.</summary>
    [ObservableProperty] private bool _isEditingTitle;

    /// <summary>Draft title value while the inline editor is open.</summary>
    [ObservableProperty] private string _editingTitle;

    /// <summary>True while the inline "add card" input row is visible at the bottom of the column.</summary>
    [ObservableProperty] private bool _isAddingCard;

    /// <summary>New card title typed in the inline add-card input.</summary>
    [ObservableProperty] private string _newCardTitle = string.Empty;

    // ── Forwarded model props for XAML bindings ───────────────────────────────

    public string Id    => Column.Id;
    public string Title => Column.Title;

    /// <summary>Live card count driven by the Cards collection.</summary>
    public int CardCount => Cards.Count;

    public KanbanColumnViewModel(KanbanColumn column)
    {
        Column = column;
        _editingTitle = column.Title;
        Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CardCount));
    }

    /// <summary>Commits the in-place title edit, updating the model title.</summary>
    public void CommitTitleEdit()
    {
        var trimmed = EditingTitle.Trim();
        if (!string.IsNullOrEmpty(trimmed))
            Column.Title = trimmed;
        else
            EditingTitle = Column.Title;  // revert on empty input
        IsEditingTitle = false;
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Cancels the in-place title edit, reverting to the current model title.</summary>
    public void CancelTitleEdit()
    {
        EditingTitle = Column.Title;
        IsEditingTitle = false;
    }
}
