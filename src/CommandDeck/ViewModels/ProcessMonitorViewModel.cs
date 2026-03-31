using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the process monitor view.
/// </summary>
public partial class ProcessMonitorViewModel : ObservableObject, IDisposable
{
    private readonly IProcessMonitorService _processMonitorService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notificationService;
    private HashSet<int> _knownPids = new();

    [ObservableProperty]
    private ObservableCollection<ProcessInfo> _processes = new();

    [ObservableProperty]
    private ProcessInfo? _selectedProcess;

    [ObservableProperty]
    private bool _isMonitoring;

    [ObservableProperty]
    private int _totalProcesses;

    [ObservableProperty]
    private long _totalMemoryMb;

    [ObservableProperty]
    private string _lastRefreshed = "Never";

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ProcessInfo> _filteredProcesses = new();

    public ProcessMonitorViewModel(
        IProcessMonitorService processMonitorService,
        IDialogService dialogService,
        INotificationService notificationService)
    {
        _processMonitorService = processMonitorService;
        _dialogService = dialogService;
        _notificationService = notificationService;
        _processMonitorService.ProcessesUpdated += OnProcessesUpdated;
    }

    /// <summary>
    /// Starts monitoring processes.
    /// </summary>
    [RelayCommand]
    public void StartMonitoring()
    {
        if (IsMonitoring) return;
        _processMonitorService.StartMonitoring(TimeSpan.FromSeconds(10));
        IsMonitoring = true;
    }

    /// <summary>
    /// Stops monitoring processes.
    /// </summary>
    [RelayCommand]
    public void StopMonitoring()
    {
        _processMonitorService.StopMonitoring();
        IsMonitoring = false;
    }

    /// <summary>
    /// Manually refreshes the process list.
    /// </summary>
    [RelayCommand]
    private async Task RefreshProcesses()
    {
        var processes = await _processMonitorService.GetRunningProcessesAsync();
        UpdateProcessList(processes);
    }

    /// <summary>
    /// Kills the selected process.
    /// </summary>
    [RelayCommand]
    private async Task KillProcess(ProcessInfo? process)
    {
        if (process == null) return;

        bool confirmed = await _dialogService.ConfirmAsync(
            $"Encerrar processo '{process.Name}' (PID: {process.Pid})?",
            "Confirmar Encerramento");

        if (confirmed)
        {
            bool killed = await _processMonitorService.KillProcessAsync(process.Pid);
            if (killed)
            {
                Processes.Remove(process);
                ApplyFilter();
            }
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    private void OnProcessesUpdated(List<ProcessInfo> processes)
    {
        // BeginInvoke (async) avoids blocking the background monitor thread
        Application.Current?.Dispatcher.BeginInvoke(() => UpdateProcessList(processes));
    }

    private void UpdateProcessList(List<ProcessInfo> processes)
    {
        // Detect new and dead processes
        var currentPids = new HashSet<int>(processes.Select(p => p.Pid));

        if (_knownPids.Count > 0)
        {
            // New processes
            var newProcesses = processes.Where(p => !_knownPids.Contains(p.Pid)).ToList();
            foreach (var np in newProcesses)
            {
                _notificationService.Notify(
                    $"{np.Name} detectado (PID {np.Pid})",
                    NotificationType.Info,
                    NotificationSource.Process,
                    message: np.Port > 0 ? $"Porta: {np.Port}" : null);
            }

            // Dead processes
            var deadPids = _knownPids.Except(currentPids).ToList();
            if (deadPids.Count > 0)
            {
                _notificationService.Notify(
                    $"{deadPids.Count} processo(s) encerrado(s)",
                    NotificationType.Warning,
                    NotificationSource.Process);
            }
        }
        _knownPids = currentPids;

        // Repopulate in-place to avoid full visual tree rebuild
        Processes.Clear();
        foreach (var p in processes) Processes.Add(p);

        TotalProcesses = processes.Count;
        TotalMemoryMb = processes.Sum(p => p.MemoryUsageMb);
        LastRefreshed = DateTime.Now.ToString("HH:mm:ss");
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var source = string.IsNullOrWhiteSpace(FilterText)
            ? Processes
            : (IEnumerable<ProcessInfo>)Processes.Where(p =>
                p.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                p.CommandLine.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                p.Pid.ToString().Contains(FilterText));

        // Repopulate in-place
        FilteredProcesses.Clear();
        foreach (var p in source) FilteredProcesses.Add(p);
    }

    public void Dispose()
    {
        _processMonitorService.ProcessesUpdated -= OnProcessesUpdated;
        StopMonitoring();
        GC.SuppressFinalize(this);
    }
}
