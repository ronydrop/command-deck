using System;
using System.Collections.Generic;
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
        var provider = _assistantService.GetActiveProvider()
                    ?? _assistantService.ActiveProvider;
        if (provider is null)
            return new OrbProviderInfo("none", "AI", "#6C7086");

        return new OrbProviderInfo(
            provider.Name ?? provider.ProviderName,
            provider.DisplayName,
            provider.DisplayColor);
    }

    public bool HasActiveProvider()
    {
        var provider = _assistantService.GetActiveProvider() ?? _assistantService.ActiveProvider;
        return provider is not null && provider.IsConfigured && provider.IsAvailable;
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

        var response = await _assistantService.ChatAsync(messages);
        return response.IsError ? string.Empty : (response.Content ?? string.Empty).Trim();
    }

    public async Task CopyContextToClipboardAsync()
    {
        var context = await _contextService.GetActiveTerminalContextAsync();
        if (context == null)
        {
            await Application.Current!.Dispatcher.InvokeAsync(() =>
                Clipboard.SetText("No active terminal context."));
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

        await Application.Current!.Dispatcher.InvokeAsync(() =>
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
        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Be concise and practical."),
            AssistantMessage.User(message)
        };

        var response = await _assistantService.ChatAsync(messages);
        return response.IsError ? $"Erro: {response.Error}" : (response.Content ?? string.Empty);
    }

    public Point GetSavedPosition() => new Point(32, 32); // canto superior esquerdo (consistente com AppSettings)

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

    public async Task<(bool IsEnabled, bool IsPositionLocked)> LoadOrbDisplaySettingsAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            return (settings.IsAiOrbEnabled, settings.IsAiOrbPositionLocked);
        }
        catch
        {
            return (true, false);
        }
    }

    private async Task PersistPositionAsync(Point position)  // fire-and-forget wrapper
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
