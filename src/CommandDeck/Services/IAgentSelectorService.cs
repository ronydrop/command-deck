using CommandDeck.Models;

namespace CommandDeck.Services;

public interface IAgentSelectorService
{
    IReadOnlyList<AgentGroup> Groups { get; }
    IReadOnlyList<AgentDefinition> Agents { get; }
    AgentDefinition? ActiveAgent { get; }
    void SelectAgent(string agentId);
    event Action<AgentDefinition>? AgentChanged;
}
