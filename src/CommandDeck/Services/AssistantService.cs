using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Facade implementation of IAssistantService.
/// Receives all registered IAssistantProvider instances via DI multiregistration
/// and delegates calls to whichever provider is currently active.
/// </summary>
public sealed class AssistantService : IAssistantService
{
    private readonly IReadOnlyList<IAssistantProvider> _providers;
    private readonly AssistantSettings _settings;
    private IAssistantProvider _active;
    private readonly IDatabaseService _db;
    private readonly IClaudeUsageService _usageService;

    // ─── Centralized provider name ↔ enum mapping ─────────────────────────
    private static readonly Dictionary<string, AssistantProviderType> ProvidersByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["OpenAI"]      = AssistantProviderType.OpenAI,
            ["Anthropic"]   = AssistantProviderType.Anthropic,
            ["Ollama"]      = AssistantProviderType.Ollama,
            ["OpenRouter"]  = AssistantProviderType.OpenRouter,
        };

    // ─── Expanded WSL state ───────────────────────────────────────────────
    private readonly Dictionary<string, IAssistantProvider> _providersByName = new(StringComparer.OrdinalIgnoreCase);
    private bool _isDisposed;
    public event Action<AssistantResponse>? AssistantResponseReceived;

    public AssistantService(
        IEnumerable<IAssistantProvider> providers,
        AssistantSettings settings,
        IDatabaseService db,
        IClaudeUsageService usageService)
    {
        _providers = providers.ToList();
        _settings = settings;
        _db = db;
        _usageService = usageService;

        // Build name lookup for WSL-style access
        foreach (var p in _providers)
        {
            var name = p.Name ?? p.ProviderName;
            _providersByName[name] = p;
        }

        // Boot with the provider matching the persisted setting, falling back to first available.
        _active = ResolveProvider(_settings.ActiveProvider)
                  ?? _providers.FirstOrDefault()
                  ?? throw new InvalidOperationException(
                      "No IAssistantProvider implementations were registered.");
    }

    // ─── IAssistantService (original WIN members) ─────────────────────────

    public IAssistantProvider ActiveProvider => _active;

    public bool IsAnyProviderAvailable => _providers.Any(p => p.IsAvailable);

    public async Task<string> ExplainTerminalOutputAsync(string output, CancellationToken ct = default)
    {
        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Explain the following terminal output concisely."),
            AssistantMessage.User(output)
        };
        var response = await ChatAsync(messages);
        return response.IsError ? $"AI Error: {response.Error}" : response.Content ?? string.Empty;
    }

    public async Task<string> SuggestCommandAsync(string description, string? shell = null, CancellationToken ct = default)
    {
        var shellPart = shell is not null ? $" Target shell: {shell}." : string.Empty;
        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Suggest a shell command. Reply with only the command."),
            AssistantMessage.User($"Task: {description}{shellPart}")
        };
        var response = await ChatAsync(messages);
        return response.IsError ? $"AI Error: {response.Error}" : response.Content ?? string.Empty;
    }

    /// <inheritdoc/>
    public async Task<AssistantResponse> ChatAsync(IReadOnlyList<AssistantMessage> messages)
    {
        await _active.InitializeAsync();

        if (!_active.IsConfigured)
            return AssistantResponse.Failed("Provider ativo não está configurado.");

        if (!_active.IsAvailable)
            return AssistantResponse.Failed("Provider ativo não está disponível.");

        var response = await _active.ChatAsync(messages);
        ReportUsage(response);
        return response;
    }

    public async IAsyncEnumerable<AssistantResponse> StreamChatAsync(
        IReadOnlyList<AssistantMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var response in _active.StreamChatAsync(messages))
        {
            ct.ThrowIfCancellationRequested();
            ReportUsage(response);
            yield return response;
        }
    }

    /// <summary>
    /// Switches the active provider to the one matching <paramref name="type"/>.
    /// If no registered provider maps to that type, the current provider is kept.
    /// </summary>
    public void SwitchProvider(AssistantProviderType type)
    {
        var found = ResolveProvider(type);
        if (found is not null)
        {
            _active = found;
            _settings.ActiveProvider = type;
            // GetCurrentModel() already reads from the just-updated ActiveProvider
            _ = _db.SaveAssistantPreferencesAsync(found.ProviderName, GetCurrentModel() ?? string.Empty);
        }
    }

    public async Task RestorePreferencesAsync()
    {
        var prefs = await _db.GetAssistantPreferencesAsync();
        if (prefs is null) return;

        // Use centralized mapping; unknown names fall back to Ollama
        var providerType = ProvidersByName.GetValueOrDefault(
            prefs.Value.providerName,
            AssistantProviderType.Ollama);

        var found = ResolveProvider(providerType);
        if (found is not null)
        {
            _active = found;
            _settings.ActiveProvider = providerType;
            ApplyModelToSettings(providerType, prefs.Value.model);
        }
    }

    // ─── IAssistantService (expanded WSL members) ─────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<IAssistantProvider> GetProviders() => _providers;

    /// <inheritdoc/>
    public Task SetActiveProviderAsync(string providerName)
    {
        if (!_providersByName.TryGetValue(providerName, out var provider))
            throw new InvalidOperationException(
                $"Provider '{providerName}' is not registered. Available: {string.Join(", ", _providersByName.Keys)}");

        _active = provider;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IAssistantProvider? GetActiveProvider() => _active;

    /// <inheritdoc/>
    public string ActiveProviderDisplayName => _active?.DisplayName ?? _active?.ProviderName ?? "None";

    /// <inheritdoc/>
    public void RegisterProvider(IAssistantProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var name = provider.Name ?? provider.ProviderName;
        _providersByName[name] = provider;
    }

    /// <inheritdoc/>
    public Task<string> ExplainTerminalOutputAsync(string output)
        => ExplainTerminalOutputAsync(output, CancellationToken.None);

    /// <inheritdoc/>
    public async Task<List<string>> SuggestCommandsAsync(string context)
    {
        var result = await SuggestCommandAsync(context);
        return string.IsNullOrWhiteSpace(result)
            ? new List<string>()
            : result.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <inheritdoc/>
    public Task InitializeAsync()
    {
        // Initialize all providers that support it
        var tasks = _providers.Select(p => p.InitializeAsync()).ToArray();
        return Task.WhenAll(tasks);
    }

    // ─── Usage tracking helpers ───────────────────────────────────────────

    private void ReportUsage(AssistantResponse response)
    {
        AssistantResponseReceived?.Invoke(response);
        if (response.Usage is { } u)
            _usageService.TrackUsage(u.PromptTokens, u.CompletionTokens, GetCurrentModel());
    }

    private string? GetCurrentModel() =>
        _settings.ActiveProvider switch
        {
            AssistantProviderType.Anthropic  => _settings.AnthropicModel,
            AssistantProviderType.OpenAI     => _settings.OpenAIModel,
            AssistantProviderType.OpenRouter => _settings.OpenRouterModel,
            _                                => _settings.OllamaModel
        };

    // ─── IDisposable ──────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        foreach (var provider in _providers)
        {
            try { (provider as IDisposable)?.Dispose(); }
            catch (Exception ex) { Debug.WriteLine($"[AssistantService] Provider dispose failed: {ex.Message}"); }
        }
    }

    // ─── Settings bridge ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public void ApplySettings(string provider, string model, string baseUrl, string apiKey, string? anthropicAuthMode = null)
    {
        // Normalize Settings-screen aliases ("local" → "Ollama") then use centralized mapping
        var normalizedProvider = provider?.ToLowerInvariant() switch
        {
            "local" => "Ollama",
            _       => provider
        };
        var providerType = ProvidersByName.GetValueOrDefault(
            normalizedProvider ?? string.Empty,
            AssistantProviderType.None);

        // Update the shared AssistantSettings object (singleton, used by providers)
        switch (providerType)
        {
            case AssistantProviderType.OpenAI:
                _settings.OpenAIModel = !string.IsNullOrWhiteSpace(model) ? model : _settings.OpenAIModel;
                _settings.OpenAIKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : _settings.OpenAIKey;
                break;
            case AssistantProviderType.Anthropic:
                _settings.AnthropicModel = !string.IsNullOrWhiteSpace(model) ? model : _settings.AnthropicModel;
                _settings.AnthropicKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : _settings.AnthropicKey;
                _settings.AnthropicAuth = anthropicAuthMode == "claude_oauth"
                    ? AnthropicAuthMode.ClaudeOAuth
                    : AnthropicAuthMode.ApiKey;
                break;
            case AssistantProviderType.OpenRouter:
                _settings.OpenRouterModel = !string.IsNullOrWhiteSpace(model) ? model : _settings.OpenRouterModel;
                _settings.OpenRouterKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : _settings.OpenRouterKey;
                break;
            case AssistantProviderType.Ollama:
                _settings.OllamaModel = !string.IsNullOrWhiteSpace(model) ? model : _settings.OllamaModel;
                if (!string.IsNullOrWhiteSpace(baseUrl))
                    _settings.OllamaBaseUrl = baseUrl;
                break;
        }

        _settings.ActiveProvider = providerType;

        // Switch to the matching provider (if one exists)
        if (providerType != AssistantProviderType.None)
        {
            var found = ResolveProvider(providerType);
            if (found is not null)
            {
                _active = found;
                var modelName = GetCurrentModel();
                _ = _db.SaveAssistantPreferencesAsync(found.ProviderName, modelName ?? string.Empty);
            }
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Writes <paramref name="model"/> into the correct <see cref="AssistantSettings"/> property
    /// for the given provider type. Centralizes the per-provider model field assignment.
    /// </summary>
    private void ApplyModelToSettings(AssistantProviderType providerType, string model)
    {
        switch (providerType)
        {
            case AssistantProviderType.Anthropic:
                _settings.AnthropicModel = model;
                break;
            case AssistantProviderType.OpenAI:
                _settings.OpenAIModel = model;
                break;
            case AssistantProviderType.OpenRouter:
                _settings.OpenRouterModel = model;
                break;
            default:
                _settings.OllamaModel = model;
                break;
        }
    }

    private IAssistantProvider? ResolveProvider(AssistantProviderType type)
    {
        // Derive provider name from the centralized dictionary (reverse lookup)
        var targetName = ProvidersByName
            .FirstOrDefault(kv => kv.Value == type).Key;

        if (targetName is null)
            return null;

        // Try instance-level name lookup first, then fall back to ProviderName scan
        if (_providersByName.TryGetValue(targetName, out var found))
            return found;

        return _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, targetName, StringComparison.OrdinalIgnoreCase));
    }
}
