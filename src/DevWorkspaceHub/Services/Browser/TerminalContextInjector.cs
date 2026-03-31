using System.Text;
using System.Text.RegularExpressions;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.Services.Browser;

public static partial class TerminalContextInjector
{
    private const int MaxLength = 8000;
    private const int ChunkSize = 1024;
    private static readonly TimeSpan ChunkDelay = TimeSpan.FromMilliseconds(10);

    public static async Task InjectAsync(
        ITerminalService terminalService,
        string sessionId,
        string contextText)
    {
        var sanitized = SanitizeText(contextText);

        if (sanitized.Length > MaxLength)
            sanitized = sanitized[..MaxLength] + "\n... [truncated]";

        // Leading newline to separate from existing content
        await terminalService.WriteAsync(sessionId, "\n");

        // Chunked write to avoid overwhelming the terminal buffer
        var bytes = Encoding.UTF8.GetBytes(sanitized);
        for (var offset = 0; offset < bytes.Length; offset += ChunkSize)
        {
            var length = Math.Min(ChunkSize, bytes.Length - offset);
            var chunk = Encoding.UTF8.GetString(bytes, offset, length);
            await terminalService.WriteAsync(sessionId, chunk);

            if (offset + length < bytes.Length)
                await Task.Delay(ChunkDelay);
        }

        // Trailing newline
        await terminalService.WriteAsync(sessionId, "\n");
    }

    private static string SanitizeText(string text)
    {
        // Strip ANSI escape sequences
        text = AnsiEscapeRegex().Replace(text, string.Empty);

        // Remove control characters except \n and \t
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c) && c != '\n' && c != '\t')
                continue;
            sb.Append(c);
        }

        return sb.ToString().Replace("\r\n", "\n").Replace("\r", "\n");
    }

    [GeneratedRegex(@"\x1b\[[0-9;]*[a-zA-Z]|\x1b\][^\x07]*\x07|\x1b[^[\]].?")]
    private static partial Regex AnsiEscapeRegex();
}
