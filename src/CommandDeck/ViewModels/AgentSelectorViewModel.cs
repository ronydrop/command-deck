using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

public partial class AgentItemViewModel : ObservableObject
{
    public AgentDefinition Definition { get; init; } = null!;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Icon => Definition.Icon;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class AgentGroupViewModel : ObservableObject
{
    public string Label { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public ObservableCollection<AgentItemViewModel> Items { get; init; } = new();
}

public partial class AgentSelectorViewModel : ObservableObject
{
    private readonly IAgentSelectorService _service;
    private readonly IAiTerminalLauncher _launcher;

    public ObservableCollection<AgentGroupViewModel> Groups { get; } = new();

    [ObservableProperty]
    private string _activeAgentName = "Claude Code";

    [ObservableProperty]
    private string _activeAgentIcon = "\U0001F7E3";

    [ObservableProperty]
    private bool _isOpen;

    public AgentSelectorViewModel(IAgentSelectorService service, IAiTerminalLauncher launcher)
    {
        _service = service;
        _launcher = launcher;

        foreach (var group in service.Groups)
        {
            var groupVm = new AgentGroupViewModel
            {
                Label = group.Label,
                Icon = group.Icon
            };

            foreach (var agent in service.Agents.Where(a => a.Group == group.Key))
            {
                groupVm.Items.Add(new AgentItemViewModel
                {
                    Definition = agent,
                    IsSelected = agent.Id == (service.ActiveAgent?.Id ?? "cc")
                });
            }

            Groups.Add(groupVm);
        }

        SyncActiveDisplay();

        service.AgentChanged += _ =>
        {
            UpdateSelectionState();
            SyncActiveDisplay();
        };
    }

    [RelayCommand]
    private async Task SelectAgent(string agentId)
    {
        _service.SelectAgent(agentId);
        IsOpen = false;

        var agent = _service.ActiveAgent;
        if (agent is not null)
        {
            await _launcher.LaunchAsync(agent.SessionType, agent.ModelOrAlias);
        }
    }

    [RelayCommand]
    private void ToggleOpen() => IsOpen = !IsOpen;

    private void UpdateSelectionState()
    {
        var activeId = _service.ActiveAgent?.Id;
        foreach (var group in Groups)
            foreach (var item in group.Items)
                item.IsSelected = item.Definition.Id == activeId;
    }

    private void SyncActiveDisplay()
    {
        var active = _service.ActiveAgent;
        ActiveAgentName = active?.Name ?? "Claude Code";
        ActiveAgentIcon = active?.Icon ?? "\U0001F7E3";
    }
}
