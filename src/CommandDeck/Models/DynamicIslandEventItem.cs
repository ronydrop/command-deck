using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommandDeck.Models;

/// <summary>
/// Priority levels for Dynamic Island events.
/// Higher values surface first in the island.
/// </summary>
public enum IslandEventPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Presentation model for an actionable event displayed in the Dynamic Island.
/// Replaces the session-centric approach with an event-centric one.
/// </summary>
public partial class DynamicIslandEventItem : ObservableObject
{
    public DynamicIslandEventItem()
    {
        ChoiceOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ChoiceOptions));
    }

    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; init; } = string.Empty;

    /// <summary>When set, matches <see cref="NotificationItem.Id"/> for feed deduplication.</summary>
    public string? SourceNotificationId { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>When set, the event auto-expires after this time.</summary>
    public DateTime? ExpiresAt { get; init; }

    [ObservableProperty]
    private IslandEventPriority _priority = IslandEventPriority.Normal;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _previewText = string.Empty;

    /// <summary>Second line: tool argument, error snippet, etc. (Phase 3 preview).</summary>
    [ObservableProperty]
    private string _actionDetail = string.Empty;

    /// <summary>Main snippet rendered in the expanded event card.</summary>
    [ObservableProperty]
    private string _primarySnippet = string.Empty;

    /// <summary>Secondary supporting line rendered below the main snippet.</summary>
    [ObservableProperty]
    private string _secondarySnippet = string.Empty;

    /// <summary>Session or thread title shown as contextual metadata.</summary>
    [ObservableProperty]
    private string _sessionTitle = string.Empty;

    /// <summary>Compact badge shown in the pill to clarify the event type.</summary>
    [ObservableProperty]
    private string _compactBadge = string.Empty;

    /// <summary>Whether the snippets should be rendered as markdown.</summary>
    [ObservableProperty]
    private bool _supportsMarkdown;

    /// <summary>True when the event can navigate to a specific terminal context.</summary>
    [ObservableProperty]
    private bool _canJumpToExactContext;

    [ObservableProperty]
    private string _icon = string.Empty;

    /// <summary>Agent name (e.g. "Claude", "Codex", "Gemini") — empty for non-AI events.</summary>
    [ObservableProperty]
    private string _agentLabel = string.Empty;

    [ObservableProperty]
    private AiAgentState _agentState = AiAgentState.Idle;

    [ObservableProperty]
    private NotificationType _severity = NotificationType.Info;

    [ObservableProperty]
    private DynamicIslandEventKind _eventKind = DynamicIslandEventKind.Activity;

    [ObservableProperty]
    private DynamicIslandVisualTone _accentTone = DynamicIslandVisualTone.Neutral;

    /// <summary>Label for the primary CTA button (e.g. "Allow", "Abrir").</summary>
    [ObservableProperty]
    private string? _primaryActionLabel;

    /// <summary>Label for the secondary CTA button (e.g. "Deny", "Ignorar").</summary>
    [ObservableProperty]
    private string? _secondaryActionLabel;

    /// <summary>Command invoked by the primary CTA.</summary>
    [ObservableProperty]
    private IRelayCommand? _primaryActionCommand;

    /// <summary>Command invoked by the secondary CTA.</summary>
    [ObservableProperty]
    private IRelayCommand? _secondaryActionCommand;

    /// <summary>Multiple-choice answers for <see cref="AiAgentState.WaitingInput"/> (when parsed from terminal).</summary>
    public ObservableCollection<DynamicIslandChoiceOption> ChoiceOptions { get; } = new();

    /// <summary>True if this event has expired.</summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

    public bool IsDecisionEvent => EventKind is DynamicIslandEventKind.Approval or DynamicIslandEventKind.Question;
    public bool HasSecondarySnippet => !string.IsNullOrWhiteSpace(SecondarySnippet);
    public bool HasSessionTitle => !string.IsNullOrWhiteSpace(SessionTitle);
}
