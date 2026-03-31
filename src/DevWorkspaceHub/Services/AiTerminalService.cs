using System.Diagnostics;
using System.Text.RegularExpressions;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

public sealed class AiTerminalService : IAiTerminalService
{
    private readonly ITerminalSessionService _sessionService;
    private readonly ITerminalService _terminalService;

    private AiCliInfo? _cachedCliInfo;

    // Prompt patterns to detect when the shell is ready for input
    private static readonly Regex[] PromptPatterns =
    {
        new(@"[#$%>\]]\s*$", RegexOptions.Compiled),
        new(@"PS>\s*$", RegexOptions.Compiled),
    };

    public AiTerminalService(ITerminalSessionService sessionService, ITerminalService terminalService)
    {
        _sessionService = sessionService;
        _terminalService = terminalService;
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

        // Wait for the shell to print its prompt before injecting the command.
        // This replaces the old fixed Task.Delay(400) which was unreliable
        // (too short for cold WSL starts, too long for warm shells).
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnOutput(string sid, string output)
        {
            if (sid != sessionId) return;
            var trimmed = output.TrimEnd();
            foreach (var pattern in PromptPatterns)
            {
                if (pattern.IsMatch(trimmed))
                {
                    tcs.TrySetResult(true);
                    return;
                }
            }
        }

        _terminalService.OutputReceived += OnOutput;
        try
        {
            // Wait up to 8 seconds for the shell prompt; fallback: inject anyway
            await Task.WhenAny(tcs.Task, Task.Delay(8000));
        }
        finally
        {
            _terminalService.OutputReceived -= OnOutput;
        }

        // Small settle delay so the shell's line editor is fully initialized
        await Task.Delay(100);
        await _sessionService.WriteAsync(sessionId, command + "\r");
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
