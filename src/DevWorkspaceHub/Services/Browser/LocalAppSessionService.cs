using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace DevWorkspaceHub.Services.Browser;

public class LocalAppSessionService : ILocalAppSessionService
{
    private static readonly int[] CommonPorts = { 3000, 3001, 5173, 5174, 8080, 8000, 4200, 5000, 5001 };

    public async Task<int?> DetectPortAsync(string projectPath, string? projectType = null)
    {
        var port = await TryDetectFromConfigFiles(projectPath);
        if (port.HasValue && await IsPortAvailableAsync(port.Value))
            return port.Value;

        foreach (var p in CommonPorts)
        {
            if (await IsPortAvailableAsync(p))
                return p;
        }

        return null;
    }

    public async Task<bool> IsPortAvailableAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("localhost", port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(1000));
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public string GetLocalUrl(int port) => $"http://localhost:{port}";

    private async Task<int?> TryDetectFromConfigFiles(string projectPath)
    {
        var packageJsonPath = Path.Combine(projectPath, "package.json");
        if (File.Exists(packageJsonPath))
        {
            var content = await File.ReadAllTextAsync(packageJsonPath);
            var portMatch = Regex.Match(content, @"--port[=\s]+(\d+)");
            if (portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out var p))
                return p;
            var portMatch2 = Regex.Match(content, @"PORT[=:]\s*(\d+)");
            if (portMatch2.Success && int.TryParse(portMatch2.Groups[1].Value, out var p2))
                return p2;
        }

        var envPath = Path.Combine(projectPath, ".env");
        if (File.Exists(envPath))
        {
            var lines = await File.ReadAllLinesAsync(envPath);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"^PORT\s*=\s*(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var p))
                    return p;
            }
        }

        return null;
    }
}
