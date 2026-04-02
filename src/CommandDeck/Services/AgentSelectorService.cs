using System.Linq;
using CommandDeck.Models;

namespace CommandDeck.Services;

public sealed class AgentSelectorService : IAgentSelectorService
{
    private readonly ISettingsService _settingsService;
    private string _activeAgentId = "claude";

    public event Action<AgentDefinition>? AgentChanged;

    public IReadOnlyList<AgentGroup> Groups { get; } = new List<AgentGroup>
    {
        new() { Key = "models", Label = "AI Models",  Icon = "\U0001F916" },
        new() { Key = "agents", Label = "Code Agents", Icon = "\u2699" }
    };

    public IReadOnlyList<AgentDefinition> Agents { get; } = new List<AgentDefinition>
    {
        new() { Id = "claude",        Name = "Claude",        Description = "Conversa com Claude",           Icon = "\U0001F7E2", Group = "models", SessionType = AiSessionType.Claude        },
        new() { Id = "claude-resume", Name = "Claude Resume", Description = "Retomar sessão Claude",         Icon = "\U0001F504", Group = "models", SessionType = AiSessionType.ClaudeResume  },
        new() { Id = "codex",         Name = "Codex",         Description = "OpenAI Codex agent",            Icon = "\U0001F535", Group = "agents", SessionType = AiSessionType.Codex         },
        new() { Id = "aider",         Name = "Aider",         Description = "Aider pair programming",        Icon = "\U0001F7E0", Group = "agents", SessionType = AiSessionType.Aider         },
        new() { Id = "gemini",        Name = "Gemini",        Description = "Google Gemini CLI",              Icon = "\U0001F7E1", Group = "agents", SessionType = AiSessionType.Gemini        },
        new() { Id = "copilot",       Name = "Copilot",       Description = "GitHub Copilot CLI",            Icon = "\U0001F7E3", Group = "agents", SessionType = AiSessionType.Copilot       },
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
