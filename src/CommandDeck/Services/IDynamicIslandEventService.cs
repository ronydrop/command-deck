using System.Collections.ObjectModel;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Aggregates events from AI sessions, notifications and terminal state
/// into a prioritized feed consumed by <see cref="ViewModels.DynamicIslandViewModel"/>.
/// </summary>
public interface IDynamicIslandEventService
{
    /// <summary>All active events ordered by priority (highest first).</summary>
    ReadOnlyObservableCollection<DynamicIslandEventItem> Events { get; }

    /// <summary>The highest-priority event currently active, or null.</summary>
    DynamicIslandEventItem? PrimaryEvent { get; }

    /// <summary>Fired when the primary event changes.</summary>
    event Action<DynamicIslandEventItem?>? PrimaryEventChanged;

    /// <summary>Fired when any event is added.</summary>
    event Action<DynamicIslandEventItem>? EventAdded;

    /// <summary>Fired when any event is removed.</summary>
    event Action<DynamicIslandEventItem>? EventRemoved;
}
