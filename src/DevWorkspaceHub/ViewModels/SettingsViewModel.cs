using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel para a tela de configurações.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private string _terminalFontFamily = "Cascadia Code, Consolas, Courier New";

    [ObservableProperty]
    private double _terminalFontSize = 14.0;

    [ObservableProperty]
    private ShellType _defaultShell = ShellType.WSL;

    [ObservableProperty]
    private int _gitRefreshIntervalSeconds = 5;

    [ObservableProperty]
    private int _processMonitorIntervalSeconds = 3;

    [ObservableProperty]
    private bool _startWithLastProject = true;

    /// <summary>
    /// Shells disponíveis para o ComboBox.
    /// </summary>
    public IReadOnlyList<ShellType> AvailableShells { get; } =
        Enum.GetValues<ShellType>().ToList();

    /// <summary>
    /// Disparado quando o diálogo deve ser fechado. True = salvo, False = cancelado.
    /// </summary>
    public event Action<bool>? CloseRequested;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Carrega as configurações do serviço.
    /// </summary>
    public async Task InitializeAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        TerminalFontFamily = settings.TerminalFontFamily;
        TerminalFontSize = settings.TerminalFontSize;
        DefaultShell = settings.DefaultShell;
        GitRefreshIntervalSeconds = settings.GitRefreshIntervalSeconds;
        ProcessMonitorIntervalSeconds = settings.ProcessMonitorIntervalSeconds;
        StartWithLastProject = settings.StartWithLastProject;
    }

    /// <summary>
    /// Salva as configurações e fecha o diálogo.
    /// </summary>
    [RelayCommand]
    private async Task Save()
    {
        var settings = await _settingsService.GetSettingsAsync();
        settings.TerminalFontFamily = TerminalFontFamily;
        settings.TerminalFontSize = TerminalFontSize;
        settings.DefaultShell = DefaultShell;
        settings.GitRefreshIntervalSeconds = GitRefreshIntervalSeconds;
        settings.ProcessMonitorIntervalSeconds = ProcessMonitorIntervalSeconds;
        settings.StartWithLastProject = StartWithLastProject;
        await _settingsService.SaveSettingsAsync(settings);
        CloseRequested?.Invoke(true);
    }

    /// <summary>
    /// Cancela sem salvar.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(false);
    }

    /// <summary>
    /// Restaura os valores padrão e recarrega.
    /// </summary>
    [RelayCommand]
    private async Task ResetDefaults()
    {
        await _settingsService.ResetToDefaultsAsync();
        await InitializeAsync();
    }
}
