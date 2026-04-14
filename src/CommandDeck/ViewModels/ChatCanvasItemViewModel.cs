using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// Canvas item ViewModel for an AI chat tile.
/// Supports multiple independent instances, each with its own conversation history,
/// provider preference, and persistent storage via IDatabaseService.
/// </summary>
public partial class ChatCanvasItemViewModel : CanvasItemViewModel, IDisposable
{
    private readonly IAssistantService _assistant;
    private readonly INotificationService _notificationService;
    private readonly ISettingsService _settingsService;
    private readonly ISecretStorageService _secretStorageService;
    private readonly IClaudeOAuthService _claudeOAuthService;
    private readonly IDatabaseService _db;
    private readonly AssistantSettings _assistantSettings;

    private CancellationTokenSource? _cts;
    private string _conversationId;

    private static readonly TimeSpan AvailabilityCacheDuration = TimeSpan.FromSeconds(30);
    private DateTime _lastAvailabilityCheck = DateTime.MinValue;
    private bool _isAnthropicOAuthMode;

    // ─── Observable properties ────────────────────────────────────────────────

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Pronto";
    [ObservableProperty] private string _providerInfo = string.Empty;
    [ObservableProperty] private bool _isProviderAvailable;
    [ObservableProperty] private AssistantProviderType _selectedProvider;
    [ObservableProperty] private string _selectedProviderLabel = "Nenhum";
    [ObservableProperty] private ObservableCollection<string> _availableModels = new();
    [ObservableProperty] private string _selectedModel = string.Empty;
    [ObservableProperty] private string _chatTitle = "Chat AI";

    /// <summary>Conversation history shown in the chat list.</summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public override CanvasItemType ItemType => CanvasItemType.ChatWidget;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public ChatCanvasItemViewModel(
        CanvasItemModel model,
        IAssistantService assistant,
        INotificationService notificationService,
        ISettingsService settingsService,
        ISecretStorageService secretStorageService,
        IClaudeOAuthService claudeOAuthService,
        IDatabaseService db,
        AssistantSettings assistantSettings)
        : base(model)
    {
        _assistant = assistant;
        _notificationService = notificationService;
        _settingsService = settingsService;
        _secretStorageService = secretStorageService;
        _claudeOAuthService = claudeOAuthService;
        _db = db;
        _assistantSettings = assistantSettings;

        // Restore conversationId from metadata or create a new one
        _conversationId = model.Metadata.TryGetValue("conversationId", out var cid) && !string.IsNullOrEmpty(cid)
            ? cid
            : Guid.NewGuid().ToString("N");
        model.Metadata["conversationId"] = _conversationId;

        // Restore title from metadata
        if (model.Metadata.TryGetValue("chatTitle", out var title) && !string.IsNullOrEmpty(title))
            _chatTitle = title;

        _settingsService.SettingsChanged += OnSettingsChanged;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await RefreshProviderInfoAsync(force: true);
        await LoadHistoryAsync();
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessage()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        InputText = string.Empty;
        await ExecuteWithProviderGuardAsync(async ct =>
        {
            var messages = new List<AssistantMessage>();

            var settings = await _settingsService.GetSettingsAsync();
            if (!string.IsNullOrWhiteSpace(settings.AiSystemPrompt))
                messages.Add(AssistantMessage.System(settings.AiSystemPrompt));

            foreach (var m in Messages)
                messages.Add(m.IsUser ? AssistantMessage.User(m.Content) : AssistantMessage.Assistant(m.Content));
            messages.Add(AssistantMessage.User(text));

            messages = await Task.Run(() => TrimToContextWindow(messages), ct);

            Messages.Add(ChatMessage.FromUser(text));
            _ = _db.SaveChatMessageAsync(_conversationId, "user", text, GetCurrentModelName(), GetCurrentProviderName())
                .ContinueWith(t => System.Diagnostics.Debug.WriteLine($"[ChatTile] SaveMsg failed: {t.Exception?.InnerException?.Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);

            var loadingMsg = ChatMessage.Loading();
            Messages.Add(loadingMsg);

            var sb = new StringBuilder();
            try
            {
                var lastUiUpdate = DateTime.UtcNow;
                loadingMsg.IsStreaming = true;
                await foreach (var response in _assistant.StreamChatAsync((IReadOnlyList<AssistantMessage>)messages, ct))
                {
                    if (response.IsError)
                    {
                        sb.Append($"\n\n[Erro: {response.Error}]");
                        break;
                    }
                    sb.Append(response.Content);
                    if ((DateTime.UtcNow - lastUiUpdate).TotalMilliseconds >= 80)
                    {
                        loadingMsg.Content = sb.ToString();
                        lastUiUpdate = DateTime.UtcNow;
                    }
                }
                loadingMsg.Content = sb.ToString();
                _ = _db.SaveChatMessageAsync(_conversationId, "assistant", loadingMsg.Content, GetCurrentModelName(), GetCurrentProviderName())
                    .ContinueWith(t => System.Diagnostics.Debug.WriteLine($"[ChatTile] SaveMsg failed: {t.Exception?.InnerException?.Message}"),
                        TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (OperationCanceledException)
            {
                sb.Append("[cancelado]");
                loadingMsg.Content = sb.ToString();
            }
            catch (Exception ex)
            {
                sb.Append($"\n\n[Erro: {ex.Message}]");
                loadingMsg.Content = sb.ToString();
            }
            finally
            {
                loadingMsg.IsStreaming = false;
            }
        });
    }

    private bool CanSendMessage() => !IsLoading && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand]
    private void ClearHistory()
    {
        Messages.Clear();
        _conversationId = Guid.NewGuid().ToString("N");
        Model.Metadata["conversationId"] = _conversationId;
        StatusText = "Nova conversa iniciada.";
    }

    [RelayCommand]
    private void CancelRequest()
    {
        _cts?.Cancel();
        StatusText = "Cancelado.";
    }

    [RelayCommand]
    private async Task RetryLastMessage()
    {
        ChatMessage? lastUser = null;
        ChatMessage? lastAssistant = null;
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            if (lastAssistant is null && !Messages[i].IsUser && Messages[i].HasError)
                lastAssistant = Messages[i];
            else if (lastUser is null && Messages[i].IsUser)
                lastUser = Messages[i];
            if (lastUser is not null && lastAssistant is not null) break;
        }

        if (lastUser is null || lastAssistant is null) return;

        Messages.Remove(lastAssistant);
        Messages.Remove(lastUser);

        InputText = lastUser.Content;
        await SendMessage();
    }

    [RelayCommand]
    private void SwitchProvider(AssistantProviderType providerType)
    {
        _assistant.SwitchProvider(providerType);
        SelectedProvider = providerType;
        _ = RefreshProviderInfoAsync(force: true);
    }

    /// <summary>
    /// Appends a message to the chat (used by ChatTileRouter to inject context from outside).
    /// </summary>
    public void ReceiveMessage(string content, bool isUser = false)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Messages.Add(isUser ? ChatMessage.FromUser(content) : ChatMessage.FromAssistant(content));
        });
    }

    /// <summary>
    /// Injects a prompt and optionally auto-sends it.
    /// Used by ChatTileRouter when routing AI quick actions to this tile.
    /// </summary>
    public async Task InjectPromptAsync(string prompt, bool autoSend = false)
    {
        await System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            InputText = prompt;
            if (autoSend)
                await SendMessage();
        });
    }

    // ─── Property callbacks ───────────────────────────────────────────────────

    partial void OnInputTextChanged(string value) => SendMessageCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value) => SendMessageCommand.NotifyCanExecuteChanged();

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

    partial void OnSelectedModelChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        switch (_assistantSettings.ActiveProvider)
        {
            case AssistantProviderType.Anthropic:  _assistantSettings.AnthropicModel  = value; break;
            case AssistantProviderType.OpenAI:     _assistantSettings.OpenAIModel     = value; break;
            case AssistantProviderType.OpenRouter: _assistantSettings.OpenRouterModel = value; break;
            case AssistantProviderType.Ollama:     _assistantSettings.OllamaModel     = value; break;
        }
        _ = PersistModelAsync(value);
    }

    partial void OnChatTitleChanged(string value)
    {
        Model.Metadata["chatTitle"] = value;
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task PersistModelAsync(string model)
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.AiModel = model;
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
            System.Diagnostics.Debug.WriteLine($"[ChatTile] PersistModel failed: {ex.Message}");
        }
    }

    private async Task RefreshProviderInfoAsync(bool force = false)
    {
        try
        {
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

            ProviderInfo = isOAuthMode && available
                ? $"Claude ({_claudeOAuthService.GetSubscriptionType() ?? "OAuth"}) • {modelName}"
                : available
                    ? $"{providerName} • {modelName} • Online"
                    : $"{providerName} • Indisponível";

            StatusText = available ? "Pronto"
                : SelectedProvider switch
                {
                    AssistantProviderType.Ollama => "Ollama indisponível. Execute 'ollama serve'.",
                    AssistantProviderType.Anthropic => "Anthropic indisponível. Verifique a API key.",
                    AssistantProviderType.OpenRouter => "OpenRouter indisponível. Verifique a API key.",
                    _ => "OpenAI indisponível. Verifique a API key."
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

    private void RefreshAvailableModels()
    {
        var models = _assistantSettings.ActiveProvider switch
        {
            AssistantProviderType.Anthropic => new[] { "claude-sonnet-4-6", "claude-opus-4-6", "claude-sonnet-4-5-20241022", "claude-haiku-4-5-20251001" },
            AssistantProviderType.OpenAI => new[] { "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-3.5-turbo", "o3-mini", "o1-mini" },
            AssistantProviderType.OpenRouter => new[]
            {
                "anthropic/claude-sonnet-4.6", "anthropic/claude-opus-4.6",
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

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            AvailableModels.Clear();
            foreach (var m in models) AvailableModels.Add(m);
            SelectedModel = AvailableModels.Contains(currentModel) ? currentModel : (AvailableModels.FirstOrDefault() ?? string.Empty);
        });
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var records = await _db.GetChatMessagesAsync(_conversationId);
            await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                foreach (var r in records)
                    Messages.Add(r.Role == "user"
                        ? ChatMessage.FromUser(r.Content)
                        : ChatMessage.FromAssistant(r.Content));
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatTile] LoadHistory failed: {ex.Message}");
        }
    }

    private async Task ExecuteWithProviderGuardAsync(Func<CancellationToken, Task> action)
    {
        if (!IsProviderAvailable)
        {
            var hint = SelectedProvider switch
            {
                AssistantProviderType.Ollama => "Verifique se o Ollama está em execução: 'ollama serve'",
                AssistantProviderType.OpenAI => "Verifique sua API key do OpenAI nas Configurações.",
                AssistantProviderType.Anthropic when _isAnthropicOAuthMode => "Claude Code não encontrado. Instale e faça login.",
                AssistantProviderType.Anthropic => "Verifique sua API key da Anthropic nas Configurações.",
                AssistantProviderType.OpenRouter => "Verifique sua API key do OpenRouter nas Configurações.",
                _ => "Configure um provider nas Configurações (Ctrl+,)."
            };
            StatusText = hint;
            _notificationService.Notify("Provider de IA indisponível", NotificationType.Error, NotificationSource.AI);
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
            _notificationService.Notify("IA respondeu", NotificationType.Success, NotificationSource.AI,
                message: _assistant.ActiveProvider?.ProviderName ?? "IA");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelado.";
        }
        catch (Exception ex)
        {
            StatusText = $"Erro: {ex.Message}";
            _notificationService.Notify("Erro na IA", NotificationType.Error, NotificationSource.AI, message: ex.Message);
            System.Diagnostics.Debug.WriteLine($"[ChatTile] {ex}");
            await RefreshProviderInfoAsync(force: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private List<AssistantMessage> TrimToContextWindow(List<AssistantMessage> messages)
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

        static int Estimate(string text) => Math.Max(1, text.Length / 4);

        var totalTokens = messages.Sum(m => Estimate(m.Content));
        if (totalTokens <= tokenBudget) return messages;

        var systemMessages = messages.Where(m => m.Role == AssistantRole.System).ToList();
        var otherMessages  = messages.Where(m => m.Role != AssistantRole.System).ToList();

        var systemTokens = systemMessages.Sum(m => Estimate(m.Content));
        var remaining = tokenBudget - systemTokens;

        var kept = new List<AssistantMessage>();
        for (var i = otherMessages.Count - 1; i >= 0; i--)
        {
            var t = Estimate(otherMessages[i].Content);
            if (remaining - t < 0) break;
            remaining -= t;
            kept.Insert(0, otherMessages[i]);
        }

        return [.. systemMessages, .. kept];
    }

    private void OnSettingsChanged(AppSettings settings) => _ = ApplyAndRefreshAsync(settings);

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
                    "anthropic"  => "ai_anthropic_api_key",
                    "openrouter" => "ai_openrouter_api_key",
                    _            => "ai_openai_api_key"
                };
                try { apiKey = await _secretStorageService.RetrieveSecretAsync(secretName) ?? string.Empty; }
                catch { /* secure storage may not be available */ }
            }

            _assistant.ApplySettings(settings.AiProvider ?? "none", settings.AiModel ?? string.Empty,
                settings.AiBaseUrl ?? string.Empty, apiKey, settings.AnthropicAuthMode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatTile] ApplySettings failed: {ex.Message}");
        }

        await RefreshProviderInfoAsync(force: true);
    }

    private string GetCurrentModelName() => _assistantSettings.ActiveProvider switch
    {
        AssistantProviderType.Anthropic  => _assistantSettings.AnthropicModel,
        AssistantProviderType.OpenAI     => _assistantSettings.OpenAIModel,
        AssistantProviderType.OpenRouter => _assistantSettings.OpenRouterModel,
        AssistantProviderType.Ollama     => _assistantSettings.OllamaModel,
        _                                => string.Empty
    };

    private string GetCurrentProviderName() => _assistantSettings.ActiveProvider switch
    {
        AssistantProviderType.Anthropic  => "anthropic",
        AssistantProviderType.OpenAI     => "openai",
        AssistantProviderType.OpenRouter => "openrouter",
        AssistantProviderType.Ollama     => "ollama",
        _                                => "none"
    };

    public void Dispose()
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
