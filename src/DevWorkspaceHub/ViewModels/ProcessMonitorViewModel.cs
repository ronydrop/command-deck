using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel for the process monitor view.
/// </summary>
public partial class ProcessMonitorViewModel : ObservableObject, IDisposable
{
    private readonly IProcessMonitorService _processMonitorService;

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

    public ProcessMonitorViewModel(IProcessMonitorService processMonitorService)
    {
        _processMonitorService = processMonitorService;
        _processMonitorService.ProcessesUpdated += OnProcessesUpdated;
    }

    /// <summary>
    /// Starts monitoring processes.
    /// </summary>
    [RelayCommand]
    public void StartMonitoring()
    {
        if (IsMonitoring) return;
        _processMonitorService.StartMonitoring(TimeSpan.FromSeconds(3));
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

        var result = MessageBox.Show(
            $"Kill process '{process.Name}' (PID: {process.Pid})?",
            "Confirm Kill",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
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
        Application.Current?.Dispatcher.Invoke(() => UpdateProcessList(processes));
    }

    private void UpdateProcessList(List<ProcessInfo> processes)
    {
        Processes = new ObservableCollection<ProcessInfo>(processes);
        TotalProcesses = processes.Count;
        TotalMemoryMb = processes.Sum(p => p.MemoryUsageMb);
        LastRefreshed = DateTime.Now.ToString("HH:mm:ss");
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            FilteredProcesses = new ObservableCollection<ProcessInfo>(Processes);
        }
        else
        {
            var filtered = Processes.Where(p =>
                p.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                p.CommandLine.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                p.Pid.ToString().Contains(FilterText))
                .ToList();
            FilteredProcesses = new ObservableCollection<ProcessInfo>(filtered);
        }
    }

    public void Dispose()
    {
        _processMonitorService.ProcessesUpdated -= OnProcessesUpdated;
        StopMonitoring();
        GC.SuppressFinalize(this);
    }
}
