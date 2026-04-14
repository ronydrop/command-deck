using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommandDeck.Models;

namespace CommandDeck.ViewModels;

/// <summary>
/// Presentation wrapper for a <see cref="KanbanCard"/> that adds transient
/// UI state (expand/collapse, just-moved flash, inline edit drafts) without polluting the model.
/// </summary>
public partial class KanbanCardViewModel : ObservableObject
{
    /// <summary>The underlying data model. Used for persistence calls and read access.</summary>
    public KanbanCard Card { get; }

    // ── Transient UI state ───────────────────────────────────────────────────

    /// <summary>Whether the card detail panel is expanded inline.</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>
    /// Flashes true for 1.5 s after the card is moved to trigger the green glow animation.
    /// Reset by a timer in <see cref="WidgetCanvasItemViewModel"/>.
    /// </summary>
    [ObservableProperty] private bool _justMoved;

    /// <summary>Active tab in the expanded detail panel: "overview", "progress", or "notes".</summary>
    [ObservableProperty] private string _activeTab = "overview";

    /// <summary>True while the user is double-click-editing the card title inline.</summary>
    [ObservableProperty] private bool _isEditingTitle;

    // ── Draft fields (populated by BeginEdit, committed by CommitEdit) ───────

    [ObservableProperty] private string _draftTitle       = string.Empty;
    [ObservableProperty] private string _draftDescription = string.Empty;
    [ObservableProperty] private string _draftInstructions = string.Empty;
    [ObservableProperty] private string _draftAgent       = string.Empty;
    [ObservableProperty] private string _draftModel       = string.Empty;
    [ObservableProperty] private string _draftColor       = string.Empty;

    // ── Model delegation — for XAML bindings on the collapsed card display ───

    public string Id       => Card.Id;
    public string Title    => Card.Title;
    public string Agent    => Card.Agent;
    public string Color    => Card.Color;
    public bool   Launched => Card.Launched;
    public bool   HasDeps  => Card.CardRefs.Count > 0;

    public List<KanbanComment> Comments => Card.Comments;

    public KanbanCardViewModel(KanbanCard card) => Card = card;

    // ── Edit lifecycle ───────────────────────────────────────────────────────

    /// <summary>
    /// Copies the current model values into draft fields so the user can edit
    /// without immediately mutating the model.
    /// Called automatically when the panel expands.
    /// </summary>
    public void BeginEdit()
    {
        DraftTitle        = Card.Title;
        DraftDescription  = Card.Description;
        DraftInstructions = Card.Instructions;
        DraftAgent        = Card.Agent;
        DraftModel        = Card.Model;
        DraftColor        = Card.Color;
    }

    /// <summary>
    /// Writes draft values back to the model and notifies the UI.
    /// Returns the mutated <see cref="KanbanCard"/> ready for persistence.
    /// </summary>
    public KanbanCard CommitEdit()
    {
        Card.Title        = DraftTitle.Trim().Length > 0 ? DraftTitle.Trim() : Card.Title;
        Card.Description  = DraftDescription;
        Card.Instructions = DraftInstructions;
        Card.Agent        = DraftAgent;
        Card.Model        = DraftModel;
        Card.Color        = DraftColor;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Agent));
        OnPropertyChanged(nameof(Color));
        return Card;
    }

    /// <summary>
    /// Called by the source generator when <see cref="IsExpanded"/> changes.
    /// Populates draft fields whenever the panel opens.
    /// </summary>
    partial void OnIsExpandedChanged(bool value)
    {
        if (value) BeginEdit();
    }

    /// <summary>Exposes the protected base method publicly for use by parent view-models.</summary>
    public new void OnPropertyChanged(string propertyName) => base.OnPropertyChanged(propertyName);

    // ── Update from service event ────────────────────────────────────────────

    /// <summary>
    /// Propagates updated model data from the service and notifies the UI.
    /// Called when <see cref="IKanbanService.CardUpdated"/> fires for this card's ID.
    /// </summary>
    public void UpdateFrom(KanbanCard updated)
    {
        Card.Title        = updated.Title;
        Card.Description  = updated.Description;
        Card.Instructions = updated.Instructions;
        Card.Color        = updated.Color;
        Card.Agent        = updated.Agent;
        Card.Model        = updated.Model;
        Card.Launched     = updated.Launched;
        Card.ColumnId     = updated.ColumnId;
        Card.SortOrder    = updated.SortOrder;
        Card.UpdatedAt    = updated.UpdatedAt;
        Card.FileRefs     = updated.FileRefs;
        Card.CardRefs     = updated.CardRefs;
        Card.Comments     = updated.Comments;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Agent));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Launched));
        OnPropertyChanged(nameof(HasDeps));
        OnPropertyChanged(nameof(Comments));

        // Keep drafts in sync if the panel is already open
        if (IsExpanded) BeginEdit();
    }
}
