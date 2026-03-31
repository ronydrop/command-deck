using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Service for monitoring running development processes.
/// </summary>
public interface IProcessMonitorService
{
    /// <summary>
    /// Gets a snapshot of all monitored development processes.
    /// </summary>
    Task<List<ProcessInfo>> GetRunningProcessesAsync();

    /// <summary>
    /// Kills a process by PID.
    /// </summary>
    Task<bool> KillProcessAsync(int pid);

    /// <summary>
    /// Starts periodic monitoring.
    /// </summary>
    void StartMonitoring(TimeSpan interval);

    /// <summary>
    /// Stops periodic monitoring.
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Event raised when the process list is refreshed.
    /// </summary>
    event Action<List<ProcessInfo>>? ProcessesUpdated;
}
