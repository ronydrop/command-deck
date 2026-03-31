using System.Diagnostics;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

public sealed class AiTerminalService : IAiTerminalService
{
    private readonly ITerminalSessionService _sessionService;

    private AiCliInfo? _cachedCliInfo;

    public AiTerminalService(ITerminalSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public async Task<bool> IsCliAvailableAsync(string cliName = "cc")
    {
        var info = await DetectCliAsync();
        return cliName switch
        {
            "cc" => info.CcAvailable,
            "claude" => info.ClaudeAvailable,
            _ => false
        };
    }

    public Task<AiCliInfo> DetectCliAsync()
    {
        if (_cachedCliInfo is not null)
            return Task.FromResult(_cachedCliInfo);

        var ccAvailable = CheckCommandExists("cc");
        var claudeAvailable = CheckCommandExists("claude");

        string? ccVersion = ccAvailable ? GetCommandVersion("cc --version") : null;
        string? claudeVersion = claudeAvailable ? GetCommandVersion("claude --version") : null;

        _cachedCliInfo = new AiCliInfo(
            CcAvailable: ccAvailable,
            ClaudeAvailable: claudeAvailable,
            CcVersion: ccVersion,
            ClaudeVersion: claudeVersion,
            OpenRouterConfigured: ccAvailable);

        return Task.FromResult(_cachedCliInfo);
    }

    public string GetLaunchCommand(AiSessionType sessionType, string? modelOrAlias = null)
    {
        return sessionType switch
        {
            AiSessionType.Cc => "cc",
            AiSessionType.CcRun => $"cc run {modelOrAlias ?? "sonnet"}",
            AiSessionType.CcOpenRouter => "cc or",
            AiSessionType.Claude => "claude",
            AiSessionType.ClaudeResume => "claude --resume",
            _ => string.Empty
        };
    }

    public async Task InjectAiCommandAsync(string sessionId, AiSessionType sessionType, string? modelOrAlias = null)
    {
        var command = GetLaunchCommand(sessionType, modelOrAlias);
        if (string.IsNullOrEmpty(command))
            return;

        await Task.Delay(400);
        await _sessionService.WriteAsync(sessionId, command + "\n");
    }

    public void TagSession(TerminalSessionModel session, AiSessionType sessionType, string? modelOrAlias = null)
    {
        session.AiSessionType = sessionType;
        session.AiModelUsed = sessionType switch
        {
            AiSessionType.Cc => "default",
            AiSessionType.CcRun => modelOrAlias ?? "sonnet",
            AiSessionType.CcOpenRouter => "openrouter",
            AiSessionType.Claude => "claude",
            AiSessionType.ClaudeResume => "claude-resume",
            _ => string.Empty
        };
        session.Title = sessionType switch
        {
            AiSessionType.Cc => "AI • cc",
            AiSessionType.CcRun => $"AI • {modelOrAlias ?? "sonnet"}",
            AiSessionType.CcOpenRouter => "AI • OpenRouter",
            AiSessionType.Claude => "AI • claude",
            AiSessionType.ClaudeResume => "AI • claude --resume",
            _ => session.Title
        };
    }

    public IReadOnlyList<TerminalSessionModel> GetActiveAiSessions()
    {
        return _sessionService.GetActiveSessions()
            .Where(s => s.IsAiSession)
            .ToList()
            .AsReadOnly();
    }

    public void InvalidateCliCache() => _cachedCliInfo = null;

    private static bool CheckCommandExists(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = $"-e which {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return false;

            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetCommandVersion(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = $"-e bash -c \"{command} 2>/dev/null | head -1\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }
}
