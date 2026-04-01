using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

public enum OrbState { Idle, Hover, Active, Recording, Processing }

/// <summary>
/// ViewModel for the AI Floating Orb widget.
/// Manages state, position, radial menu, voice input, and AI actions.
/// </summary>
public partial class AiOrbViewModel : ObservableObject
{
    private readonly IAiOrbService _orbService;
    private readonly IVoiceInputService _voiceService;

    [ObservableProperty] private OrbState _state = OrbState.Idle;
    [ObservableProperty] private double _positionX = 1316; // canto inferior direito (1400 - 56 - 28)
    [ObservableProperty] private double _positionY = 816;  // acima da barra de status (900 - 56 - 28)
    [ObservableProperty] private bool _isRadialMenuOpen;
    [ObservableProperty] private string _activeProvider = "Claude";
    [ObservableProperty] private string _activeProviderColor = "#CBA6F7";
    [ObservableProperty] private string _transcriptionText = string.Empty;
    [ObservableProperty] private string _lastSuggestion = string.Empty;
    [ObservableProperty] private bool _hasSuggestion;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isAgentSelectorOpen;

    public AiOrbViewModel(IAiOrbService orbService, IVoiceInputService voiceService)
    {
        _orbService = orbService;
        _voiceService = voiceService;

        // BeginInvoke (assíncrono) em vez de Invoke (síncrono) para evitar deadlock:
        // SpeechRecognitionEngine dispara eventos de background threads — Invoke síncrono
        // bloquearia esperando a UI thread enquanto ela pode estar bloqueada no RecognizeAsyncStop.
        _voiceService.TranscriptionUpdated += text =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() => TranscriptionText = text);
        };

        // async void não existe aqui: o delegate Action<string> não permite Task de retorno.
        // Encapsular o await dentro do BeginInvoke para evitar o padrão "async void" implícito.
        _voiceService.TranscriptionCompleted += text =>
        {
            Application.Current?.Dispatcher.BeginInvoke(async () =>
            {
                TranscriptionText = text;
                IsRecording = false;
                State = OrbState.Processing;
                try
                {
                    await ProcessTranscriptionAsync(text);
                }
                catch
                {
                    State = OrbState.Idle;
                }
            });
        };

        // Load persisted position
        var pos = _orbService.GetSavedPosition();
        PositionX = pos.X;
        PositionY = pos.Y;

        // Reflect active provider
        RefreshProvider();
    }

    [RelayCommand]
    private void ToggleRadialMenu()
    {
        // Se está gravando: click no orb serve como escape — força stop da gravação.
        // Isso evita o travamento permanente se StartRecording falhou silenciosamente.
        if (State == OrbState.Recording)
        {
            _ = ForceStopRecordingAsync();
            return;
        }
        if (State == OrbState.Processing) return;

        IsRadialMenuOpen = !IsRadialMenuOpen;
        State = IsRadialMenuOpen ? OrbState.Active : OrbState.Idle;
    }

    /// <summary>
    /// Para a gravação de forma forçada, restaurando o estado para Idle independentemente
    /// do estado interno do VoiceInputService. Usado como escape se o estado travar.
    /// </summary>
    private async Task ForceStopRecordingAsync()
    {
        try { await _voiceService.StopAndTranscribeAsync(); }
        catch { /* ignore — apenas restaurar estado */ }
        IsRecording = false;
        State = OrbState.Idle;
        TranscriptionText = string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        if (_voiceService.IsRecording) return;

        // Verificar disponibilidade do microfone ANTES de mudar qualquer estado visual.
        if (!_voiceService.IsAvailable)
        {
            StatusMessage = "Microfone não disponível";
            await Task.Delay(2000);
            StatusMessage = string.Empty;
            return;
        }

        IsRecording = true;
        IsRadialMenuOpen = false;
        State = OrbState.Recording;
        TranscriptionText = string.Empty;

        try
        {
            await _voiceService.StartRecordingAsync();

            // Se o service falhou silenciosamente (engoliu exceção mas _isRecording=false),
            // detectar e restaurar o estado do ViewModel — evita travamento permanente.
            if (!_voiceService.IsRecording)
            {
                IsRecording = false;
                State = OrbState.Idle;
                StatusMessage = "Falha ao iniciar gravação";
                await Task.Delay(2000);
                StatusMessage = string.Empty;
            }
        }
        catch (Exception ex)
        {
            IsRecording = false;
            State = OrbState.Idle;
            StatusMessage = $"Erro: {ex.Message}";
            await Task.Delay(2000);
            StatusMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        if (!_voiceService.IsRecording) return;
        await _voiceService.StopAndTranscribeAsync();
    }

    [RelayCommand]
    private async Task ImprovePromptAsync()
    {
        IsRadialMenuOpen = false;
        State = OrbState.Processing;
        StatusMessage = "Melhorando prompt...";
        try
        {
            var result = await _orbService.ImproveLastCommandAsync();
            LastSuggestion = result;
            HasSuggestion = !string.IsNullOrEmpty(result);
            StatusMessage = HasSuggestion ? "Sugestão pronta" : "Nenhum comando no terminal";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
        }
        finally
        {
            State = OrbState.Idle;
        }
    }

    [RelayCommand]
    private async Task CopyContextAsync()
    {
        IsRadialMenuOpen = false;
        State = OrbState.Processing;
        StatusMessage = "Capturando contexto...";
        try
        {
            await _orbService.CopyContextToClipboardAsync();
            StatusMessage = "Contexto copiado!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
        }
        finally
        {
            State = OrbState.Idle;
            await Task.Delay(2000);
            StatusMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task RunSuggestionAsync()
    {
        if (string.IsNullOrEmpty(LastSuggestion)) return;
        IsRadialMenuOpen = false;
        State = OrbState.Processing;
        try
        {
            await _orbService.ExecuteCommandAsync(LastSuggestion);
            LastSuggestion = string.Empty;
            HasSuggestion = false;
            StatusMessage = "Comando executado!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
        }
        finally
        {
            State = OrbState.Idle;
            await Task.Delay(2000);
            StatusMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void ToggleAgentSelector()
    {
        IsAgentSelectorOpen = !IsAgentSelectorOpen;
    }

    [RelayCommand]
    private async Task SelectAgentAsync(string providerName)
    {
        IsAgentSelectorOpen = false;
        IsRadialMenuOpen = false;
        await _orbService.SwitchProviderAsync(providerName);
        RefreshProvider();
    }

    [RelayCommand]
    private void CloseRadialMenu()
    {
        IsRadialMenuOpen = false;
        IsAgentSelectorOpen = false;
        if (State == OrbState.Active)
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
            RefreshProvider();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiOrb] InitializeAsync failed: {ex}");
        }
    }

    private void RefreshProvider()
    {
        var info = _orbService.GetActiveProviderInfo();
        ActiveProvider = info.Name;
        ActiveProviderColor = info.GlowColor;
    }

    private async Task ProcessTranscriptionAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Application.Current?.Dispatcher.Invoke(() => State = OrbState.Idle);
            return;
        }

        try
        {
            var response = await _orbService.SendMessageToAiAsync(text);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LastSuggestion = response;
                HasSuggestion = !string.IsNullOrEmpty(response);
                State = OrbState.Idle;
            });
        }
        catch
        {
            Application.Current?.Dispatcher.Invoke(() => State = OrbState.Idle);
        }
    }
}
