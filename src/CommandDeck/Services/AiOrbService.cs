using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Implementation of IAiOrbService.
/// Coordinates IAiContextService, IAssistantService, and ITerminalService
/// to provide all AI Orb capabilities.
/// </summary>
public class AiOrbService : IAiOrbService
{
    private readonly IAssistantService _assistantService;
    private readonly IAiContextService _contextService;
    private readonly ITerminalService _terminalService;
    private readonly ISettingsService _settingsService;

    // Provider display config
    private static readonly Dictionary<string, OrbProviderInfo> ProviderInfoMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anthropic"] = new OrbProviderInfo("anthropic", "Claude", "#CBA6F7"),   // Catppuccin mauve
        ["openai"]    = new OrbProviderInfo("openai",    "GPT",    "#A6E3A1"),   // Catppuccin green
        ["local"]     = new OrbProviderInfo("local",     "Ollama", "#FAB387"),   // Catppuccin peach
        ["openrouter"]= new OrbProviderInfo("openrouter","OpenRouter","#89DCEB"),// Catppuccin sky
        ["none"]      = new OrbProviderInfo("none",      "AI",     "#6C7086"),   // Catppuccin overlay0
    };

    public AiOrbService(
        IAssistantService assistantService,
        IAiContextService contextService,
        ITerminalService terminalService,
        ISettingsService settingsService)
    {
        _assistantService = assistantService;
        _contextService = contextService;
        _terminalService = terminalService;
        _settingsService = settingsService;
    }

    public OrbProviderInfo GetActiveProviderInfo()
    {
        var providerName = _assistantService.ActiveProvider?.Name ?? "none";
        if (ProviderInfoMap.TryGetValue(providerName, out var info))
            return info;
        return ProviderInfoMap["none"];
    }

    public async Task<string> ImproveLastCommandAsync()
    {
        var context = await _contextService.GetActiveTerminalContextAsync();
        if (context == null)
            return string.Empty;

        var recentOutput = _contextService.GetRecentOutput(context.SessionId, 20);
        if (string.IsNullOrWhiteSpace(recentOutput))
            return string.Empty;

        var prompt = $"""
            You are a developer assistant. Analyze the following terminal output and suggest an improved or corrected command.
            Reply with ONLY the improved command, no explanation.

            Terminal output:
            {recentOutput}
            """;

        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Reply with the improved command only."),
            AssistantMessage.User(prompt)
        };

        var response = await _assistantService.ActiveProvider.ChatAsync(messages);
        return response.IsError ? string.Empty : (response.Content ?? string.Empty).Trim();
    }

    public async Task CopyContextToClipboardAsync()
    {
        var context = await _contextService.GetActiveTerminalContextAsync();
        if (context == null)
        {
            Clipboard.SetText("No active terminal context.");
            return;
        }

        var recentOutput = _contextService.GetRecentOutput(context.SessionId, 50);

        var contextText = $"""
            === Terminal Context ===
            Working Directory: {context.WorkingDirectory}
            Shell: {context.ShellType}
            Project: {context.ProjectName ?? "—"}
            Git Branch: {context.GitBranch ?? "—"}

            === Recent Output ===
            {recentOutput}
            """;

        Application.Current?.Dispatcher.Invoke(() =>
            Clipboard.SetText(contextText));
    }

    public async Task ExecuteCommandAsync(string command)
    {
        var context = await _contextService.GetActiveTerminalContextAsync();
        if (context?.SessionId == null) return;

        // Write the command followed by Enter
        await _terminalService.WriteAsync(context.SessionId, command + "\r");
    }

    public async Task SwitchProviderAsync(string providerName)
    {
        await _assistantService.SetActiveProviderAsync(providerName);

        // Persist to settings
        var settings = await _settingsService.GetSettingsAsync();
        settings.AiProvider = providerName;
        await _settingsService.SaveSettingsAsync(settings);
    }

    public async Task<string> SendMessageToAiAsync(string message)
    {
        if (!_assistantService.IsAnyProviderAvailable)
            return "Nenhum provider de AI configurado.";

        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Be concise and practical."),
            AssistantMessage.User(message)
        };

        var response = await _assistantService.ActiveProvider.ChatAsync(messages);
        return response.IsError ? $"Erro: {response.Error}" : (response.Content ?? string.Empty);
    }

    public Point GetSavedPosition() => new Point(1316, 816); // canto inferior direito (padrão antes de settings carregarem)

    public async Task<Point> LoadSavedPositionAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            return new Point(settings.AiOrbPositionX, settings.AiOrbPositionY);
        }
        catch
        {
            return GetSavedPosition();
        }
    }

    public void SavePosition(Point position)
    {
        // Position is persisted via AppSettings in a fire-and-forget manner
        _ = PersistPositionAsync(position);
    }

    private async Task PersistPositionAsync(Point position)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.AiOrbPositionX = position.X;
            settings.AiOrbPositionY = position.Y;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiOrbService] SavePosition failed: {ex}");
        }
    }
}
