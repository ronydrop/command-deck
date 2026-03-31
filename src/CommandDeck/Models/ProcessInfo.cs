using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandDeck.Models;

/// <summary>
/// Represents information about a running development process.
/// </summary>
public partial class ProcessInfo : ObservableObject
{
    [ObservableProperty]
    private int _pid;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _commandLine = string.Empty;

    [ObservableProperty]
    private int? _port;

    [ObservableProperty]
    private ProcessRunStatus _status = ProcessRunStatus.Running;

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private long _memoryUsageMb;

    [ObservableProperty]
    private DateTime _startTime = DateTime.Now;

    [ObservableProperty]
    private string? _projectPath;

    /// <summary>
    /// Gets a formatted uptime string.
    /// </summary>
    public string Uptime
    {
        get
        {
            var elapsed = DateTime.Now - StartTime;
            if (elapsed.TotalDays >= 1)
                return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h";
            if (elapsed.TotalHours >= 1)
                return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        }
    }

    /// <summary>
    /// Gets a formatted memory usage string.
    /// </summary>
    public string FormattedMemory => MemoryUsageMb >= 1024
        ? $"{MemoryUsageMb / 1024.0:F1} GB"
        : $"{MemoryUsageMb} MB";

    /// <summary>
    /// Gets a formatted CPU usage string.
    /// </summary>
    public string FormattedCpu => $"{CpuUsage:F1}%";
}

/// <summary>
/// Status of a monitored process.
/// </summary>
public enum ProcessRunStatus
{
    Running,
    Suspended,
    Stopped,
    Unknown
}
