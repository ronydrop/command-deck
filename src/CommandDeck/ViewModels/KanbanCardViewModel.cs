using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommandDeck.Models;

namespace CommandDeck.ViewModels;

/// <summary>
/// Presentation wrapper for a <see cref="KanbanCard"/> that adds transient
/// UI state (expand/collapse, just-moved flash) without polluting the model.
/// </summary>
public partial class KanbanCardViewModel : ObservableObject
{
    /// <summary>The underlying data model. Used for persistence calls and read access.</summary>
    public KanbanCard Card { get; }

    /// <summary>Whether the card detail panel is expanded inline.</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>
    /// Flashes true for 1.5 s after the card is moved to trigger the green glow animation.
    /// Reset by a timer in <see cref="WidgetCanvasItemViewModel"/>.
    /// </summary>
    [ObservableProperty] private bool _justMoved;

    /// <summary>Active tab in the expanded detail panel: "overview", "progress", or "notes".</summary>
    [ObservableProperty] private string _activeTab = "overview";

    // ── Model delegation — for XAML bindings on the collapsed card display ───

    public string Id       => Card.Id;
    public string Title    => Card.Title;
    public string Agent    => Card.Agent;
    public string Color    => Card.Color;
    public bool   Launched => Card.Launched;
    public bool   HasDeps  => Card.CardRefs.Count > 0;

    public List<KanbanComment> Comments => Card.Comments;

    public KanbanCardViewModel(KanbanCard card) => Card = card;

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
    }
}
