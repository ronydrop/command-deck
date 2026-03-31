using CommunityToolkit.Mvvm.ComponentModel;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// Canvas item that wraps a <see cref="TerminalViewModel"/> adding spatial properties
/// (position, size, focus) used by the canvas layout engine.
/// </summary>
public partial class TerminalCanvasItemViewModel : CanvasItemViewModel
{
    public TerminalViewModel Terminal { get; }

    public override CanvasItemType ItemType => CanvasItemType.Terminal;

    [ObservableProperty] private string _title = "Terminal";
    [ObservableProperty] private ShellType _shellType = ShellType.WSL;
    [ObservableProperty] private string? _projectId;
    [ObservableProperty] private AiSessionType _aiSessionType = AiSessionType.None;
    [ObservableProperty] private string _aiModelUsed = string.Empty;

    /// <summary>Pane state icon (⚡🔔✅) for the card header.</summary>
    [ObservableProperty] private string _paneStateIcon = string.Empty;

    /// <summary>Current pane state for styling triggers.</summary>
    [ObservableProperty] private PaneState _paneState = PaneState.Idle;

    /// <summary>Current AI agent semantic state.</summary>
    [ObservableProperty] private AiAgentState _aiAgentState = AiAgentState.Idle;

    /// <summary>Icon for the current AI agent state.</summary>
    [ObservableProperty] private string _aiAgentStateIcon = string.Empty;

    /// <summary>Short label for the current AI agent state.</summary>
    [ObservableProperty] private string _aiAgentStateLabel = string.Empty;

    public bool IsAiSession => AiSessionType != AiSessionType.None;

    public TerminalCanvasItemViewModel(TerminalViewModel terminal, CanvasItemModel model)
        : base(model)
    {
        Terminal = terminal;
        _title = terminal.Title;
        _shellType = terminal.ShellType;
        _projectId = terminal.Session?.ProjectId;

        terminal.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(TerminalViewModel.Title):
                    Title = terminal.Title;
                    break;
                case nameof(TerminalViewModel.ShellType):
                    ShellType = terminal.ShellType;
                    break;
                case nameof(TerminalViewModel.Session) when terminal.Session is not null:
                    ProjectId = terminal.Session.ProjectId;
                    break;
            }
        };
    }

    /// <summary>
    /// Updates the pane state icon from PaneStateService events.
    /// Called by the canvas ViewModel when it receives StateChanged events.
    /// </summary>
    public void UpdatePaneState(PaneStateInfo info)
    {
        PaneState = info.State;
        PaneStateIcon = info.Icon;
    }

    /// <summary>
    /// Updates the AI agent state indicator from AiAgentStateService events.
    /// </summary>
    public void UpdateAiAgentState(AiAgentStateChangedArgs args)
    {
        AiAgentState = args.State;
        AiAgentStateIcon = args.Icon;
        AiAgentStateLabel = args.Label;
    }

    public void UpdateAiMetadata(AiSessionType sessionType, string modelUsed)
    {
        AiSessionType = sessionType;
        AiModelUsed = modelUsed;
        OnPropertyChanged(nameof(IsAiSession));
    }
}
