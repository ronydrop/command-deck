using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Monitors common development processes: node, php, artisan, npm, python, docker.
/// Uses WMI/System.Management for process info and netstat for port detection.
/// </summary>
public class ProcessMonitorService : IProcessMonitorService, IDisposable
{
    // Process names to monitor
    private static readonly HashSet<string> MonitoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node", "php", "python", "python3", "dotnet", "docker",
        "npm", "npx", "yarn", "pnpm", "bun",
        "artisan", "composer", "pip",
        "ruby", "rails", "cargo", "go",
        "nginx", "apache", "mysql", "postgres", "redis-server"
    };

    private Timer? _monitorTimer;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private Dictionary<int, int>? _portMap;

    public event Action<List<ProcessInfo>>? ProcessesUpdated;

    public async Task<List<ProcessInfo>> GetRunningProcessesAsync()
    {
        var processes = new List<ProcessInfo>();

        await _scanLock.WaitAsync();
        try
        {
            // Build port map first
            _portMap = await BuildPortMapAsync();

            var systemProcesses = Process.GetProcesses();

            foreach (var proc in systemProcesses)
            {
                try
                {
                    var procName = proc.ProcessName.ToLowerInvariant();

                    // Check if this is a monitored process
                    if (!IsMonitoredProcess(procName))
                        continue;

                    var info = new ProcessInfo
                    {
                        Pid = proc.Id,
                        Name = proc.ProcessName,
                        Status = proc.Responding ? ProcessRunStatus.Running : ProcessRunStatus.Suspended
                    };

                    // Get memory usage
                    try
                    {
                        info.MemoryUsageMb = proc.WorkingSet64 / (1024 * 1024);
                    }
                    catch { }

                    // Get CPU usage (snapshot-based approximation)
                    try
                    {
                        info.CpuUsage = await GetCpuUsageAsync(proc);
                    }
                    catch
                    {
                        info.CpuUsage = 0;
                    }

                    // Get start time
                    try
                    {
                        info.StartTime = proc.StartTime;
                    }
                    catch { }

                    // Get command line (best effort)
                    try
                    {
                        info.CommandLine = GetCommandLine(proc.Id);
                    }
                    catch
                    {
                        info.CommandLine = proc.ProcessName;
                    }

                    // Check for listening port
                    if (_portMap.TryGetValue(proc.Id, out int port))
                    {
                        info.Port = port;
                    }

                    processes.Add(info);
                }
                catch
                {
                    // Skip processes we can't access
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        finally
        {
            _scanLock.Release();
        }

        return processes.OrderBy(p => p.Name).ThenBy(p => p.Pid).ToList();
    }

    public async Task<bool> KillProcessAsync(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(new CancellationTokenSource(5000).Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void StartMonitoring(TimeSpan interval)
    {
        StopMonitoring();
        _monitorTimer = new Timer(async _ =>
        {
            try
            {
                var processes = await GetRunningProcessesAsync();
                ProcessesUpdated?.Invoke(processes);
            }
            catch
            {
                // Ignore monitoring errors
            }
        }, null, TimeSpan.Zero, interval);
    }

    public void StopMonitoring()
    {
        _monitorTimer?.Dispose();
        _monitorTimer = null;
    }

    public void Dispose()
    {
        StopMonitoring();
        _scanLock.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─── Private Methods ────────────────────────────────────────────────────

    private static bool IsMonitoredProcess(string processName)
    {
        if (MonitoredProcessNames.Contains(processName))
            return true;

        // Check for common patterns
        if (processName.StartsWith("node") || processName.StartsWith("php") ||
            processName.StartsWith("python") || processName.StartsWith("dotnet") ||
            processName.StartsWith("docker"))
            return true;

        return false;
    }

    /// <summary>
    /// Approximates CPU usage by measuring TotalProcessorTime delta over a short interval.
    /// </summary>
    private static async Task<double> GetCpuUsageAsync(Process proc)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = proc.TotalProcessorTime;

            await Task.Delay(100);

            proc.Refresh();
            var endTime = DateTime.UtcNow;
            var endCpuUsage = proc.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsElapsed = (endTime - startTime).TotalMilliseconds;
            var cpuPercent = (cpuUsedMs / (Environment.ProcessorCount * totalMsElapsed)) * 100.0;

            return Math.Round(Math.Min(cpuPercent, 100.0), 1);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Builds a map of PID -> listening port by parsing netstat output.
    /// </summary>
    private static async Task<Dictionary<int, int>> BuildPortMapAsync()
    {
        var portMap = new Dictionary<int, int>();

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();

            using var cts = new CancellationTokenSource(3000);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
                return portMap;
            }

            // Parse netstat output for LISTENING entries
            var lines = output.Split('\n');
            var regex = new Regex(@"\s+TCP\s+[\d.]+:(\d+)\s+[\d.]+:\d+\s+LISTENING\s+(\d+)");

            foreach (var line in lines)
            {
                var match = regex.Match(line);
                if (match.Success &&
                    int.TryParse(match.Groups[1].Value, out int port) &&
                    int.TryParse(match.Groups[2].Value, out int pid))
                {
                    portMap.TryAdd(pid, port);
                }
            }
        }
        catch
        {
            // netstat may not be available or may fail
        }

        return portMap;
    }

    /// <summary>
    /// Gets the command line for a process using WMI.
    /// </summary>
    private static string GetCommandLine(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            using var results = searcher.Get();

            foreach (var obj in results)
            {
                var cmdLine = obj["CommandLine"]?.ToString();
                if (!string.IsNullOrEmpty(cmdLine))
                    return cmdLine;
            }
        }
        catch
        {
            // WMI may not be available
        }

        return string.Empty;
    }
}
