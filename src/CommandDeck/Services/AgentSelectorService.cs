using System.Linq;
using CommandDeck.Models;

namespace CommandDeck.Services;

public sealed class AgentSelectorService : IAgentSelectorService
{
    private readonly ISettingsService _settingsService;
    private string _activeAgentId = "cc";

    public event Action<AgentDefinition>? AgentChanged;

    public IReadOnlyList<AgentGroup> Groups { get; } = new List<AgentGroup>
    {
        new() { Key = "models",    Label = "AI Models",           Icon = "\U0001F916" },
        new() { Key = "agents",    Label = "Code Agents",         Icon = "\u2699" },
        new() { Key = "providers", Label = "External Providers",  Icon = "\U0001F517" }
    };

    public IReadOnlyList<AgentDefinition> Agents { get; } = new List<AgentDefinition>
    {
        new() { Id = "claude",        Name = "Claude",            Description = "Conversa com Claude",              Icon = "\U0001F7E2", Group = "models",    SessionType = AiSessionType.Claude },
        new() { Id = "claude-resume", Name = "Claude Resume",     Description = "Retomar sessão Claude",            Icon = "\U0001F504", Group = "models",    SessionType = AiSessionType.ClaudeResume },
        new() { Id = "cc",            Name = "Claude Code",       Description = "Agente de código padrão",          Icon = "\U0001F7E3", Group = "agents",    SessionType = AiSessionType.Cc },
        new() { Id = "cc-run",        Name = "Claude Code Run",   Description = "Executar comando com cc",          Icon = "\u25B6",     Group = "agents",    SessionType = AiSessionType.CcRun },
        new() { Id = "codex",         Name = "Codex",             Description = "OpenAI Codex agent",               Icon = "\U0001F535", Group = "agents",    SessionType = AiSessionType.Cc, ModelOrAlias = "codex" },
        new() { Id = "hermes",        Name = "Hermes Agent",      Description = "Agente Hermes autônomo",           Icon = "\u26A1",     Group = "agents",    SessionType = AiSessionType.Cc, ModelOrAlias = "agent" },
        new() { Id = "openrouter",    Name = "OpenRouter",        Description = "Múltiplos modelos via OpenRouter",  Icon = "\U0001F310", Group = "providers", SessionType = AiSessionType.CcOpenRouter }
    };

    public AgentDefinition? ActiveAgent => Agents.FirstOrDefault(a => a.Id == _activeAgentId);

    public AgentSelectorService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _ = LoadDefaultAgentAsync();
    }

    public void SelectAgent(string agentId)
    {
        var agent = Agents.FirstOrDefault(a => a.Id == agentId);
        if (agent is null) return;

        _activeAgentId = agentId;
        AgentChanged?.Invoke(agent);
        _ = PersistAsync(agentId);
    }

    private async Task LoadDefaultAgentAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            if (!string.IsNullOrEmpty(settings.DefaultAgentId) &&
                Agents.Any(a => a.Id == settings.DefaultAgentId))
            {
                _activeAgentId = settings.DefaultAgentId;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentSelectorService] Failed to load default agent from settings: {ex.Message}");
        }
    }

    private async Task PersistAsync(string agentId)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.DefaultAgentId = agentId;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentSelectorService] Failed to persist default agent '{agentId}': {ex.Message}");
        }
    }
}
