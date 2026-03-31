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
        var results = new List<ProcessInfo>();

        await _scanLock.WaitAsync();
        try
        {
            // Both maps are built with a single query each (not one per process)
            var portMapTask = BuildPortMapAsync();
            var cmdLineMapTask = BuildCommandLineMapAsync();
            await Task.WhenAll(portMapTask, cmdLineMapTask);
            _portMap = portMapTask.Result;
            var cmdLineMap = cmdLineMapTask.Result;

            var allProcesses = Process.GetProcesses();

            // Pass 1: filter monitored processes and snapshot their initial CPU time
            var monitored = new List<(Process proc, ProcessInfo info, TimeSpan startCpu, DateTime startWall)>();

            foreach (var proc in allProcesses)
            {
                try
                {
                    if (!IsMonitoredProcess(proc.ProcessName.ToLowerInvariant()))
                    {
                        proc.Dispose();
                        continue;
                    }

                    var info = new ProcessInfo
                    {
                        Pid = proc.Id,
                        Name = proc.ProcessName,
                        Status = proc.Responding ? ProcessRunStatus.Running : ProcessRunStatus.Suspended
                    };

                    try { info.MemoryUsageMb = proc.WorkingSet64 / (1024 * 1024); } catch { }
                    try { info.StartTime = proc.StartTime; } catch { }
                    info.CommandLine = cmdLineMap.TryGetValue(proc.Id, out var cmd) ? cmd : proc.ProcessName;
                    if (_portMap.TryGetValue(proc.Id, out int port)) info.Port = port;

                    var startWall = DateTime.UtcNow;
                    TimeSpan startCpu = TimeSpan.Zero;
                    try { startCpu = proc.TotalProcessorTime; } catch { }

                    monitored.Add((proc, info, startCpu, startWall));
                }
                catch
                {
                    try { proc.Dispose(); } catch { }
                }
            }

            // Single shared delay for all monitored processes (was N * 100ms before)
            if (monitored.Count > 0)
                await Task.Delay(100);

            // Pass 2: compute CPU delta and collect results
            foreach (var (proc, info, startCpu, startWall) in monitored)
            {
                try
                {
                    proc.Refresh();
                    var cpuUsedMs = (proc.TotalProcessorTime - startCpu).TotalMilliseconds;
                    var elapsedMs = (DateTime.UtcNow - startWall).TotalMilliseconds;
                    if (elapsedMs > 0)
                        info.CpuUsage = Math.Round(Math.Min(cpuUsedMs / (Environment.ProcessorCount * elapsedMs) * 100.0, 100.0), 1);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }

                results.Add(info);
            }
        }
        finally
        {
            _scanLock.Release();
        }

        return results.OrderBy(p => p.Name).ThenBy(p => p.Pid).ToList();
    }

    public async Task<bool> KillProcessAsync(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            using var cts = new CancellationTokenSource(5000);
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private TimeSpan _monitorInterval;

    public void StartMonitoring(TimeSpan interval)
    {
        StopMonitoring();
        _monitorInterval = interval;
        _monitorTimer = new Timer(OnMonitorTimerTick, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private async void OnMonitorTimerTick(object? state)
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
        finally
        {
            try { _monitorTimer?.Change(_monitorInterval, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
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
    /// Builds a PID → CommandLine map with a single WMI query (instead of one per process).
    /// </summary>
    private static Task<Dictionary<int, string>> BuildCommandLineMapAsync() =>
        Task.Run(() =>
        {
            var map = new Dictionary<int, string>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process");
                using var results = searcher.Get();

                foreach (var obj in results)
                {
                    if (obj["ProcessId"] is uint pid &&
                        obj["CommandLine"] is string cmd &&
                        !string.IsNullOrEmpty(cmd))
                    {
                        map[(int)pid] = cmd;
                    }
                }
            }
            catch
            {
                // WMI may not be available
            }
            return map;
        });
}
