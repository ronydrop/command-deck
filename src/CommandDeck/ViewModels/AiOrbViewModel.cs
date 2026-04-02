using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

public enum OrbState { Idle, MenuOpen, Recording, Processing }

/// <summary>
/// ViewModel for the AI Floating Orb widget.
/// Manages state, position, radial menu, voice input, and AI actions.
/// </summary>
public partial class AiOrbViewModel : ObservableObject, IDisposable
{
    private readonly IAiOrbService _orbService;
    private readonly IVoiceInputService _voiceService;
    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _statusMessageCts;
    private bool _disposed;

    [ObservableProperty] private OrbState _state = OrbState.Idle;
    [ObservableProperty] private double _positionX = 32;
    [ObservableProperty] private double _positionY = 32;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isPositionLocked;
    [ObservableProperty] private bool _isRadialMenuOpen;
    [ObservableProperty] private bool _isRadialMenuClosing;
    [ObservableProperty] private bool _isHovered;
    [ObservableProperty] private string _activeProvider = "Claude";
    [ObservableProperty] private string _activeProviderColor = "#CBA6F7";
    [ObservableProperty] private string _transcriptionText = string.Empty;
    [ObservableProperty] private string _lastSuggestion = string.Empty;
    [ObservableProperty] private bool _hasSuggestion;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public AiOrbViewModel(IAiOrbService orbService, IVoiceInputService voiceService, ISettingsService settingsService)
    {
        _orbService = orbService;
        _voiceService = voiceService;
        _settingsService = settingsService;

        _settingsService.SettingsChanged += OnSettingsChanged;
        _voiceService.TranscriptionUpdated += OnVoiceTranscriptionUpdated;

        var pos = _orbService.GetSavedPosition();
        PositionX = pos.X;
        PositionY = pos.Y;

        RefreshProvider();
    }

    private bool CanStartRecording() => State is not OrbState.Recording and not OrbState.Processing;
    private bool CanImprovePrompt() => State is not OrbState.Recording and not OrbState.Processing && _orbService.HasActiveProvider();
    private bool CanCopyContext() => State is not OrbState.Recording and not OrbState.Processing && _orbService.HasActiveProvider();
    private bool CanRunSuggestion() => !string.IsNullOrWhiteSpace(LastSuggestion) && State is not OrbState.Processing && _orbService.HasActiveProvider();
    private bool CanStopRecording() => State == OrbState.Recording;

    partial void OnIsRadialMenuOpenChanged(bool value)
    {
        if (!value)
        {
            if (State == OrbState.MenuOpen)
                State = OrbState.Idle;
        }
    }

    partial void OnStateChanged(OrbState value)
    {
        StartRecordingCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
        ImprovePromptCommand.NotifyCanExecuteChanged();
        CopyContextCommand.NotifyCanExecuteChanged();
        RunSuggestionCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastSuggestionChanged(string value)
    {
        RunSuggestionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleRadialMenu()
    {
        if (State == OrbState.Processing)
            return;

        if (State == OrbState.Recording)
        {
            _ = CancelRecordingInternalAsync();
            return;
        }

        if (IsRadialMenuOpen)
        {
            IsRadialMenuClosing = true;
            return;
        }

        IsRadialMenuOpen = true;
        State = OrbState.MenuOpen;
    }

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private async Task StartRecordingAsync()
    {
        if (_voiceService.IsRecording)
            return;

        if (!_voiceService.IsAvailable)
        {
            await ShowTemporaryStatusAsync("Microfone não disponível");
            return;
        }

        CancelPendingStatusMessage();
        IsRadialMenuOpen = false;
        State = OrbState.Recording;
        TranscriptionText = string.Empty;

        var result = await _voiceService.StartRecordingAsync();
        if (result.Success)
            return;

        State = OrbState.Idle;
        await ShowTemporaryStatusAsync(string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? "Falha ao iniciar gravação"
            : $"Erro: {result.ErrorMessage}");
    }

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task StopRecordingAsync()
    {
        await StopRecordingInternalAsync();
    }

    [RelayCommand(CanExecute = nameof(CanImprovePrompt))]
    private async Task ImprovePromptAsync()
    {
        CancelPendingStatusMessage();
        IsRadialMenuOpen = false;
        State = OrbState.Processing;
        StatusMessage = "Melhorando prompt...";

        try
        {
            if (!_orbService.HasActiveProvider())
            {
                State = OrbState.Idle;
                await ShowTemporaryStatusAsync("Configure um provider AI nas configurações");
                return;
            }

            var result = await _orbService.ImproveLastCommandAsync();
            LastSuggestion = result;
            HasSuggestion = !string.IsNullOrWhiteSpace(result);
            State = OrbState.Idle;
            await ShowTemporaryStatusAsync(HasSuggestion ? "Sugestão pronta" : "Nenhum comando no terminal");
        }
        catch (Exception ex)
        {
            State = OrbState.Idle;
            await ShowTemporaryStatusAsync($"Erro: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyContext))]
    private async Task CopyContextAsync()
    {
        CancelPendingStatusMessage();
        IsRadialMenuOpen = false;
        State = OrbState.Processing;
        StatusMessage = "Capturando contexto...";

        try
        {
            await _orbService.CopyContextToClipboardAsync();
            State = OrbState.Idle;
            await ShowTemporaryStatusAsync("Contexto copiado!");
        }
        catch (Exception ex)
        {
            State = OrbState.Idle;
            await ShowTemporaryStatusAsync($"Erro: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunSuggestion))]
    private async Task RunSuggestionAsync()
    {
        CancelPendingStatusMessage();
        IsRadialMenuOpen = false;
        State = OrbState.Processing;
        StatusMessage = "Executando sugestão...";

        try
        {
            await _orbService.ExecuteCommandAsync(LastSuggestion);
            ClearSuggestion();
            State = OrbState.Idle;
            await ShowTemporaryStatusAsync("Comando executado!");
        }
        catch (Exception ex)
        {
            State = OrbState.Idle;
            await ShowTemporaryStatusAsync($"Erro: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CloseRadialMenu()
    {
        IsRadialMenuOpen = false;
        IsRadialMenuClosing = false;

        if (State == OrbState.MenuOpen)
            State = OrbState.Idle;
    }

    /// <summary>Saves the current position to settings.</summary>
    public void SavePosition()
    {
        _orbService.SavePosition(new Point(PositionX, PositionY));
    }

    /// <summary>
    /// Loads persisted position from settings. Called during app startup after services are ready.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var pos = await _orbService.LoadSavedPositionAsync();
            PositionX = pos.X;
            PositionY = pos.Y;

            var (isEnabled, isLocked) = await _orbService.LoadOrbDisplaySettingsAsync();
            IsVisible = isEnabled;
            IsPositionLocked = isLocked;

            RefreshProvider();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiOrb] InitializeAsync failed: {ex}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _voiceService.TranscriptionUpdated -= OnVoiceTranscriptionUpdated;
        CancelPendingStatusMessage();
        _statusMessageCts?.Dispose();
    }

    private async Task StopRecordingInternalAsync()
    {
        CancelPendingStatusMessage();
        State = OrbState.Processing;
        StatusMessage = "Processando transcrição...";

        var result = await _voiceService.StopRecordingAsync();
        TranscriptionText = string.Empty;

        if (!result.Success)
        {
            State = OrbState.Idle;
            await ShowTemporaryStatusAsync(string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Falha ao finalizar gravação"
                : $"Erro: {result.ErrorMessage}");
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Transcript))
        {
            State = OrbState.Idle;
            await ShowTemporaryStatusAsync("Nenhuma fala detectada");
            return;
        }

        await ProcessTranscriptionAsync(result.Transcript);
    }

    private async Task CancelRecordingInternalAsync()
    {
        CancelPendingStatusMessage();

        var result = await _voiceService.CancelRecordingAsync();
        TranscriptionText = string.Empty;
        State = OrbState.Idle;

        if (!result.Success)
        {
            await ShowTemporaryStatusAsync(string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Falha ao cancelar gravação"
                : $"Erro: {result.ErrorMessage}");
            return;
        }

        await ShowTemporaryStatusAsync("Gravação cancelada");
    }

    private async Task ProcessTranscriptionAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            State = OrbState.Idle;
            return;
        }

        try
        {
            var response = await _orbService.SendMessageToAiAsync(text);
            LastSuggestion = response;
            HasSuggestion = !string.IsNullOrWhiteSpace(response);
            State = OrbState.Idle;
            await ShowTemporaryStatusAsync(HasSuggestion ? "Sugestão pronta" : "Nenhuma resposta gerada");
        }
        catch (Exception ex)
        {
            State = OrbState.Idle;
            await ShowTemporaryStatusAsync($"Erro: {ex.Message}");
        }
    }

    private async Task ShowTemporaryStatusAsync(string message, int durationMs = 2000)
    {
        CancelPendingStatusMessage();

        var cts = new CancellationTokenSource();
        _statusMessageCts = cts;
        StatusMessage = message;

        try
        {
            await Task.Delay(durationMs, cts.Token);
            if (!cts.IsCancellationRequested)
                StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled status transitions.
        }
        finally
        {
            if (ReferenceEquals(_statusMessageCts, cts))
                _statusMessageCts = null;

            cts.Dispose();
        }
    }

    private void CancelPendingStatusMessage()
    {
        _statusMessageCts?.Cancel();
        _statusMessageCts = null;
    }

    private void OnVoiceTranscriptionUpdated(string text)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => TranscriptionText = text);
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsVisible = settings.IsAiOrbEnabled;
            IsPositionLocked = settings.IsAiOrbPositionLocked;
        });
    }

    private void RefreshProvider()
    {
        var info = _orbService.GetActiveProviderInfo();
        ActiveProvider = info.DisplayName;
        ActiveProviderColor = info.GlowColor;
    }

    private void ClearSuggestion()
    {
        LastSuggestion = string.Empty;
        HasSuggestion = false;
    }
}
