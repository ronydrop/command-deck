using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Documents;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

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
    private readonly IClaudeOAuthService _claudeOAuthService;
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

    private bool _isAnthropicOAuthMode;

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

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<string> _availableModels = new();

    [ObservableProperty]
    private string _selectedModel = string.Empty;

    partial void OnSelectedModelChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        // Update the active provider's model in AssistantSettings
        switch (_assistantSettings.ActiveProvider)
        {
            case AssistantProviderType.Anthropic:  _assistantSettings.AnthropicModel  = value; break;
            case AssistantProviderType.OpenAI:     _assistantSettings.OpenAIModel     = value; break;
            case AssistantProviderType.OpenRouter: _assistantSettings.OpenRouterModel = value; break;
            case AssistantProviderType.Ollama:     _assistantSettings.OllamaModel     = value; break;
        }
        // Persist model choice
        _ = PersistModelAsync(value);
    }

    private async Task PersistModelAsync(string model)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.AiModel = model;
            // Also update per-provider field
            switch (_assistantSettings.ActiveProvider)
            {
                case AssistantProviderType.Anthropic:  settings.AnthropicProviderModel  = model; break;
                case AssistantProviderType.OpenAI:     settings.OpenAiProviderModel     = model; break;
                case AssistantProviderType.OpenRouter: settings.OpenRouterProviderModel = model; break;
                case AssistantProviderType.Ollama:     settings.OllamaProviderModel     = model; break;
            }
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AssistantPanel] PersistModel failed: {ex.Message}");
        }
    }

    private void RefreshAvailableModels()
    {
        var models = _assistantSettings.ActiveProvider switch
        {
            AssistantProviderType.Anthropic => new[] { "claude-sonnet-4-6", "claude-opus-4-6", "claude-sonnet-4-5-20241022", "claude-haiku-4-5-20251001" },
            AssistantProviderType.OpenAI => new[] { "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-3.5-turbo", "o3-mini", "o1-mini" },
            AssistantProviderType.OpenRouter => new[] {
                "anthropic/claude-sonnet-4.6", "anthropic/claude-opus-4.6",
                "anthropic/claude-sonnet-4", "anthropic/claude-opus-4",
                "openai/gpt-4o", "openai/gpt-4o-mini",
                "google/gemini-2.5-pro", "google/gemini-2.5-flash",
                "meta-llama/llama-4-maverick", "deepseek/deepseek-r1"
            },
            AssistantProviderType.Ollama => new[] { "llama3.2", "llama3.1", "mistral", "codellama", "phi3" },
            _ => Array.Empty<string>()
        };

        var currentModel = _assistantSettings.ActiveProvider switch
        {
            AssistantProviderType.Anthropic  => _assistantSettings.AnthropicModel,
            AssistantProviderType.OpenAI     => _assistantSettings.OpenAIModel,
            AssistantProviderType.OpenRouter => _assistantSettings.OpenRouterModel,
            AssistantProviderType.Ollama     => _assistantSettings.OllamaModel,
            _                                => string.Empty
        };

        // ObservableCollection must be mutated on the UI thread (CollectionView constraint).
        // RefreshProviderInfoAsync can resume on a thread-pool thread after awaiting
        // GetSettingsAsync (which uses ConfigureAwait(false)), so we always dispatch here.
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            AvailableModels.Clear();
            foreach (var m in models) AvailableModels.Add(m);
            SelectedModel = AvailableModels.Contains(currentModel) ? currentModel : (AvailableModels.FirstOrDefault() ?? string.Empty);
        });
    }

    // ─── Constructor ──────────────────────────────────────────────────────────

    private readonly AssistantSettings _assistantSettings;

    public AssistantPanelViewModel(
        IAssistantService assistant,
        IWorkspaceService workspaceService,
        INotificationService notificationService,
        ISettingsService settingsService,
        ISecretStorageService secretStorageService,
        IClaudeOAuthService claudeOAuthService,
        AssistantSettings assistantSettings)
    {
        _assistant = assistant;
        _workspaceService = workspaceService;
        _notificationService = notificationService;
        _settingsService = settingsService;
        _secretStorageService = secretStorageService;
        _claudeOAuthService = claudeOAuthService;
        _assistantSettings = assistantSettings;

        _availabilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        _availabilityTimer.Tick += (_, _) => _ = RefreshProviderInfoAsync();
        // Timer starts only when panel is opened (via OnPanelOpenedAsync)

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
            // Build conversation history and trim to the active model's context window
            var history = TrimToContextWindow(
                Messages.Select(m => (m.Role, m.Content)).Append(("user", text)).ToList());

            // Add user message to UI
            Messages.Add(ChatMessage.FromUser(text));

            // Add a loading placeholder for the assistant reply
            var loadingMsg = ChatMessage.Loading();
            Messages.Add(loadingMsg);

            // Stream chunks live into the loading message (ChatMessage is ObservableObject)
            var sb = new StringBuilder();
            try
            {
                var lastUiUpdate = DateTime.UtcNow;
                loadingMsg.IsStreaming = true;
                await foreach (var chunk in _assistant.StreamChatAsync(history, ct))
                {
                    sb.Append(chunk);
                    var now = DateTime.UtcNow;
                    if ((now - lastUiUpdate).TotalMilliseconds >= 80)
                    {
                        loadingMsg.Content = sb.ToString();
                        lastUiUpdate = now;
                    }
                }
                // Final update to ensure all content is shown
                loadingMsg.Content = sb.ToString();
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
    /// Trims <paramref name="history"/> so the total estimated token count fits within
    /// the active model's context window minus a reserve for the response.
    /// Oldest non-system messages are dropped first; system messages are always kept.
    /// </summary>
    private List<(string Role, string Content)> TrimToContextWindow(
        List<(string Role, string Content)> history)
    {
        var model = _assistantSettings.ActiveProvider switch
        {
            AssistantProviderType.Anthropic   => _assistantSettings.AnthropicModel,
            AssistantProviderType.OpenAI      => _assistantSettings.OpenAIModel,
            AssistantProviderType.OpenRouter  => _assistantSettings.OpenRouterModel,
            AssistantProviderType.Ollama      => _assistantSettings.OllamaModel,
            _                                 => string.Empty
        };

        var contextWindow = ModelContextWindows.Get(model);
        var tokenBudget = contextWindow - ModelContextWindows.ResponseReserveTokens;

        // Rough estimate: 4 chars ≈ 1 token
        static int Estimate(string text) => Math.Max(1, text.Length / 4);

        var totalTokens = history.Sum(m => Estimate(m.Content));
        if (totalTokens <= tokenBudget) return history;

        var systemMessages = history.Where(m => m.Role == "system").ToList();
        var otherMessages  = history.Where(m => m.Role != "system").ToList();

        var systemTokens = systemMessages.Sum(m => Estimate(m.Content));
        var remaining = tokenBudget - systemTokens;

        // Keep newest messages first, discard oldest until under budget
        var kept = new List<(string Role, string Content)>();
        for (var i = otherMessages.Count - 1; i >= 0; i--)
        {
            var t = Estimate(otherMessages[i].Content);
            if (remaining - t < 0) break;
            remaining -= t;
            kept.Insert(0, otherMessages[i]);
        }

        return [.. systemMessages, .. kept];
    }

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
            AssistantProviderType.Anthropic => "Claude",
            AssistantProviderType.OpenRouter => "OpenRouter",
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
                "openai"     => AssistantProviderType.OpenAI,
                "anthropic"  => AssistantProviderType.Anthropic,
                "openrouter" => AssistantProviderType.OpenRouter,
                "local"      => AssistantProviderType.Ollama,
                "ollama"     => AssistantProviderType.Ollama,
                _            => AssistantProviderType.None
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

            // For Anthropic OAuth, check if Claude Code is installed and token is valid
            var isOAuthMode = SelectedProvider == AssistantProviderType.Anthropic
                && settings.AnthropicAuthMode == "claude_oauth";
            _isAnthropicOAuthMode = isOAuthMode;

            if (isOAuthMode)
            {
                available = _claudeOAuthService.IsClaudeCodeInstalled;
                if (available)
                {
                    var token = await _claudeOAuthService.GetValidAccessTokenAsync();
                    available = !string.IsNullOrEmpty(token);
                }
            }

            IsProviderAvailable = available;
            _lastAvailabilityCheck = DateTime.UtcNow;

            var modelName = !string.IsNullOrWhiteSpace(settings.AiModel) ? settings.AiModel : "padrão";

            if (isOAuthMode && available)
            {
                var sub = _claudeOAuthService.GetSubscriptionType();
                ProviderInfo = $"Claude ({sub ?? "OAuth"}) • {modelName}";
            }
            else
            {
                ProviderInfo = available
                    ? $"{providerName} • {modelName} • Online"
                    : $"{providerName} • Indisponível";
            }

            StatusText = available
                ? "Pronto"
                : SelectedProvider switch
                {
                    AssistantProviderType.Ollama => "Ollama indisponível. Execute 'ollama serve' para iniciar.",
                    AssistantProviderType.Anthropic => "Anthropic indisponível. Verifique sua API key nas Configurações.",
                    AssistantProviderType.OpenRouter => "OpenRouter indisponível. Verifique sua API key nas Configurações.",
                    _ => "OpenAI indisponível. Verifique sua API key nas Configurações."
                };
            RefreshAvailableModels();
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
        _availabilityTimer.Start();
        await RefreshProviderInfoAsync(force: true);
    }

    /// <summary>
    /// Call when the AI assistant panel is closed to stop unnecessary polling.
    /// </summary>
    public void OnPanelClosed()
    {
        _availabilityTimer.Stop();
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
                AssistantProviderType.OpenAI => "Verifique sua API key do OpenAI nas Configurações.",
                AssistantProviderType.Anthropic when _isAnthropicOAuthMode => "Claude Code não encontrado ou token inválido. Instale o Claude Code e faça login.",
                AssistantProviderType.Anthropic => "Verifique sua API key da Anthropic nas Configurações.",
                AssistantProviderType.OpenRouter => "Verifique sua API key do OpenRouter nas Configurações.",
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
                message: _assistant.ActiveProvider?.ProviderName ?? "IA");
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

    public void ReceiveElementContext(string formattedContext)
    {
        Messages.Add(ChatMessage.FromUser($"[🔍 Elemento capturado do browser]\n\n{formattedContext}"));
        InputText = "Analise este elemento e identifique possíveis melhorias.";
        StatusText = "Contexto de elemento recebido";
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
            var providerLower = settings.AiProvider?.ToLowerInvariant() ?? "none";
            if (providerLower is "openai" or "anthropic" or "openrouter")
            {
                var secretName = providerLower switch
                {
                    "anthropic" => "ai_anthropic_api_key",
                    "openrouter" => "ai_openrouter_api_key",
                    _ => "ai_openai_api_key"
                };
                try
                {
                    apiKey = await _secretStorageService.RetrieveSecretAsync(secretName) ?? string.Empty;
                }
                catch { /* secure storage may not be available */ }
            }

            _assistant.ApplySettings(
                settings.AiProvider ?? "none",
                settings.AiModel ?? string.Empty,
                settings.AiBaseUrl ?? string.Empty,
                apiKey,
                settings.AnthropicAuthMode);
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
