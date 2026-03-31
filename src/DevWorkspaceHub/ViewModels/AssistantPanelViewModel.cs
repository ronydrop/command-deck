using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Documents;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel for the AI Assistant side-panel.
/// Handles chat messages, quick-action commands and provider switching.
/// </summary>
public partial class AssistantPanelViewModel : ObservableObject, IDisposable
{
    private readonly IAssistantService _assistant;
    private readonly IWorkspaceService _workspaceService;
    private readonly INotificationService _notificationService;
    private readonly ISettingsService _settingsService;
    private readonly ISecretStorageService _secretStorageService;
    private CancellationTokenSource? _cts;
    private readonly DispatcherTimer _availabilityTimer;

    /// <summary>Timestamp of the last successful provider availability check.</summary>
    private DateTime _lastAvailabilityCheck = DateTime.MinValue;

    /// <summary>How long to cache provider availability before re-checking.</summary>
    private static readonly TimeSpan AvailabilityCacheDuration = TimeSpan.FromSeconds(30);

    // ─── Chat messages ────────────────────────────────────────────────────────

    /// <summary>Conversation history shown in the chat list.</summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    // ─── Observable Properties ────────────────────────────────────────────────

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Pronto";

    [ObservableProperty]
    private string _providerInfo = string.Empty;

    [ObservableProperty]
    private bool _isProviderAvailable;

    [ObservableProperty]
    private AssistantProviderType _selectedProvider;

    [ObservableProperty]
    private string _selectedProviderLabel = "Nenhum";

    // ─── Constructor ──────────────────────────────────────────────────────────

    public AssistantPanelViewModel(
        IAssistantService assistant,
        IWorkspaceService workspaceService,
        INotificationService notificationService,
        ISettingsService settingsService,
        ISecretStorageService secretStorageService)
    {
        _assistant = assistant;
        _workspaceService = workspaceService;
        _notificationService = notificationService;
        _settingsService = settingsService;
        _secretStorageService = secretStorageService;

        _availabilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        _availabilityTimer.Tick += (_, _) => _ = RefreshProviderInfoAsync();
        _availabilityTimer.Start();

        // Listen for settings changes (e.g. user changed AI provider in Settings screen)
        _settingsService.SettingsChanged += OnSettingsChanged;

        // Primeiro refresh assíncrono sem bloquear o construtor
        _ = RefreshProviderInfoAsync();
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the text currently in InputText as a user message and streams the AI reply.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessage()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        InputText = string.Empty;
        await ExecuteWithProviderGuardAsync(async ct =>
        {
            // Build conversation history for the stream call
            var history = Messages
                .Select(m => (m.Role, m.Content))
                .Append(("user", text))
                .ToList();

            // Add user message to UI
            Messages.Add(ChatMessage.FromUser(text));

            // Add a loading placeholder for the assistant reply
            var loadingMsg = ChatMessage.Loading();
            Messages.Add(loadingMsg);

            // Stream chunks live into the loading message (ChatMessage is ObservableObject)
            var sb = new StringBuilder();
            try
            {
                await foreach (var chunk in _assistant.StreamChatAsync(history, ct))
                {
                    sb.Append(chunk);
                    loadingMsg.Content = sb.ToString();
                    loadingMsg.IsStreaming = true;
                }
            }
            catch (OperationCanceledException)
            {
                sb.Append("[cancelado]");
                loadingMsg.Content = sb.ToString();
            }

            loadingMsg.IsStreaming = false;
        });
    }

    private bool CanSendMessage() => !IsLoading && !string.IsNullOrWhiteSpace(InputText);

    /// <summary>
    /// Reads the last 30 lines from the active terminal's output and asks the AI to explain them.
    /// </summary>
    [RelayCommand]
    private async Task ExplainOutput()
    {
        var activeTerminal = _workspaceService.ActiveTerminal;
        if (activeTerminal?.Terminal?.OutputDocument is not FlowDocument doc)
        {
            AddSystemMessage("Nenhum terminal ativo encontrado.");
            return;
        }

        var outputText = ExtractTextFromFlowDocument(doc, maxLines: 30);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            AddSystemMessage("O terminal ativo não contém output para analisar.");
            return;
        }

        await ExecuteWithProviderGuardAsync(async ct =>
        {
            Messages.Add(ChatMessage.FromUser("Explique o output do terminal ativo:"));

            var loadingMsg = ChatMessage.Loading();
            Messages.Add(loadingMsg);

            string result;
            try
            {
                result = await _assistant.ExplainTerminalOutputAsync(outputText, ct);
            }
            catch (OperationCanceledException)
            {
                result = "[cancelado]";
            }
            catch (Exception ex)
            {
                result = $"Erro ao chamar IA: {ex.Message}";
            }

            var idx = Messages.IndexOf(loadingMsg);
            if (idx >= 0)
                Messages[idx] = ChatMessage.FromAssistant(result);
            else
                Messages.Add(ChatMessage.FromAssistant(result));
        });
    }

    /// <summary>
    /// Uses the current InputText (or prompts the user to type a description) to
    /// suggest a shell command via the AI.
    /// </summary>
    [RelayCommand]
    private async Task SuggestCommand()
    {
        var description = InputText.Trim();
        if (string.IsNullOrEmpty(description))
        {
            AddSystemMessage("Digite uma descrição no campo de entrada e clique em 'Sugerir Comando'.");
            return;
        }

        InputText = string.Empty;

        await ExecuteWithProviderGuardAsync(async ct =>
        {
            Messages.Add(ChatMessage.FromUser($"Sugira um comando para: {description}"));

            var loadingMsg = ChatMessage.Loading();
            Messages.Add(loadingMsg);

            string result;
            try
            {
                result = await _assistant.SuggestCommandAsync(description, ct: ct);
            }
            catch (OperationCanceledException)
            {
                result = "[cancelado]";
            }
            catch (Exception ex)
            {
                result = $"Erro ao chamar IA: {ex.Message}";
            }

            var idx = Messages.IndexOf(loadingMsg);
            if (idx >= 0)
                Messages[idx] = ChatMessage.FromAssistant(result);
            else
                Messages.Add(ChatMessage.FromAssistant(result));
        });
    }

    /// <summary>
    /// Clears all chat messages.
    /// </summary>
    [RelayCommand]
    private void ClearHistory()
    {
        Messages.Clear();
        StatusText = "Histórico apagado.";
    }

    /// <summary>
    /// Cancels any in-progress AI request.
    /// </summary>
    [RelayCommand]
    private void CancelRequest()
    {
        _cts?.Cancel();
        StatusText = "Cancelado.";
    }

    /// <summary>
    /// Switches the active provider at runtime.
    /// </summary>
    [RelayCommand]
    private void SwitchProvider(AssistantProviderType providerType)
    {
        _assistant.SwitchProvider(providerType);
        SelectedProvider = providerType;
        _ = RefreshProviderInfoAsync(force: true);
    }

    // ─── Property callbacks ───────────────────────────────────────────────────

    partial void OnInputTextChanged(string value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProviderChanged(AssistantProviderType value)
    {
        SelectedProviderLabel = value switch
        {
            AssistantProviderType.OpenAI => "OpenAI",
            AssistantProviderType.Ollama => "Local",
            _ => "Nenhum"
        };
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifica disponibilidade do provider em background (não bloqueia a UI thread).
    /// Skips the network check if the cached result is still fresh (within <see cref="AvailabilityCacheDuration"/>).
    /// Pass <paramref name="force"/> = true to bypass the cache (e.g. after settings change).
    /// </summary>
    private async Task RefreshProviderInfoAsync(bool force = false)
    {
        try
        {
            // Skip if cache is still valid and not forced
            if (!force && (DateTime.UtcNow - _lastAvailabilityCheck) < AvailabilityCacheDuration)
                return;

            var settings = await _settingsService.GetSettingsAsync();
            var providerSetting = settings.AiProvider?.ToLowerInvariant() ?? "none";

            SelectedProvider = providerSetting switch
            {
                "openai" => AssistantProviderType.OpenAI,
                "local"  => AssistantProviderType.Ollama,
                "ollama" => AssistantProviderType.Ollama,
                _        => AssistantProviderType.None
            };

            if (SelectedProvider == AssistantProviderType.None)
            {
                IsProviderAvailable = false;
                ProviderInfo = "Nenhum provider configurado";
                StatusText = "Configure um provider de IA nas Configurações.";
                _lastAvailabilityCheck = DateTime.UtcNow;
                return;
            }

            var provider = _assistant.ActiveProvider;
            var providerName = provider?.ProviderName ?? "IA";
            var available = await Task.Run(() => provider?.IsAvailable ?? false);
            IsProviderAvailable = available;
            _lastAvailabilityCheck = DateTime.UtcNow;

            var modelName = !string.IsNullOrWhiteSpace(settings.AiModel) ? settings.AiModel : "padrão";

            ProviderInfo = available
                ? $"{providerName} • {modelName} • Online"
                : $"{providerName} • Indisponível";

            StatusText = available
                ? "Pronto"
                : SelectedProvider == AssistantProviderType.Ollama
                    ? "Ollama indisponível. Execute 'ollama serve' para iniciar."
                    : "OpenAI indisponível. Verifique sua API key nas Configurações.";
        }
        catch (Exception ex)
        {
            IsProviderAvailable = false;
            ProviderInfo = "Erro ao verificar provider";
            StatusText = $"Erro: {ex.Message}";
        }
    }

    /// <summary>
    /// Call when the AI assistant panel becomes visible to refresh provider status on-demand.
    /// Forces a fresh check regardless of cache age.
    /// </summary>
    public async Task OnPanelOpenedAsync()
    {
        await RefreshProviderInfoAsync(force: true);
    }

    /// <summary>
    /// Runs the given async action inside IsLoading=true/false guards with a fresh CancellationToken.
    /// Uses cached provider availability instead of refreshing on every call.
    /// </summary>
    private async Task ExecuteWithProviderGuardAsync(Func<CancellationToken, Task> action)
    {
        // Use cached IsProviderAvailable — refreshed by timer or OnPanelOpenedAsync()
        if (!IsProviderAvailable)
        {
            var hint = SelectedProvider switch
            {
                AssistantProviderType.Ollama => "Verifique se o Ollama está em execução: 'ollama serve'",
                AssistantProviderType.OpenAI => "Verifique sua API key nas Configurações.",
                _ => "Configure um provider nas Configurações (Ctrl+,)."
            };
            StatusText = hint;
            _notificationService.Notify(
                "Provider de IA indisponível",
                NotificationType.Error,
                NotificationSource.AI);
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsLoading = true;
        StatusText = "Processando...";

        try
        {
            await action(ct);
            StatusText = "Pronto";
            _notificationService.Notify(
                "IA respondeu",
                NotificationType.Success,
                NotificationSource.AI,
                message: _assistant.ActiveProvider.ProviderName);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelado.";
        }
        catch (Exception ex)
        {
            StatusText = $"Erro: {ex.Message}";
            _notificationService.Notify(
                "Erro na IA",
                NotificationType.Error,
                NotificationSource.AI,
                message: ex.Message);
            System.Diagnostics.Debug.WriteLine($"[AssistantPanel] {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Extracts plain text from a FlowDocument, up to <paramref name="maxLines"/> lines.
    /// </summary>
    private static string ExtractTextFromFlowDocument(FlowDocument doc, int maxLines)
    {
        var lines = new List<string>();

        try
        {
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            var fullText = range.Text ?? string.Empty;

            // Split into lines and take the last N
            var allLines = fullText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lines.AddRange(allLines.TakeLast(maxLines));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtractText] {ex.Message}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Adds an informational/system message to the chat (shown as assistant message).
    /// </summary>
    private void AddSystemMessage(string text)
    {
        Messages.Add(ChatMessage.FromAssistant(text));
    }

    /// <summary>
    /// Called when SettingsService fires SettingsChanged (e.g. after user saves Settings).
    /// Re-applies AI configuration to the AssistantService and refreshes provider info
    /// in the chat panel so it reflects the new AI configuration.
    /// </summary>
    private void OnSettingsChanged(AppSettings settings)
    {
        // Re-apply AI settings from the saved AppSettings to the AssistantService.
        // This ensures changes from ANY source (not just SettingsViewModel) are picked up.
        _ = ApplyAndRefreshAsync(settings);
    }

    /// <summary>
    /// Reads the API key from secure storage and applies the full AI configuration
    /// from the given AppSettings to the AssistantService, then refreshes the UI.
    /// </summary>
    private async Task ApplyAndRefreshAsync(AppSettings settings)
    {
        try
        {
            var apiKey = string.Empty;
            if (string.Equals(settings.AiProvider, "openai", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    apiKey = await _secretStorageService.RetrieveSecretAsync("ai_openai_api_key") ?? string.Empty;
                }
                catch { /* secure storage may not be available */ }
            }

            _assistant.ApplySettings(
                settings.AiProvider ?? "none",
                settings.AiModel ?? string.Empty,
                settings.AiBaseUrl ?? string.Empty,
                apiKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AssistantPanel] ApplySettings on change failed: {ex.Message}");
        }

        await RefreshProviderInfoAsync(force: true);
    }

    public void Dispose()
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _availabilityTimer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
