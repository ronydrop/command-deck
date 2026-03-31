using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

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

    // ─── Expanded WSL state ───────────────────────────────────────────────
    private readonly Dictionary<string, IAssistantProvider> _providersByName = new(StringComparer.OrdinalIgnoreCase);
    private bool _isDisposed;
    public event Action<AssistantResponse>? AssistantResponseReceived;

    public AssistantService(
        IEnumerable<IAssistantProvider> providers,
        AssistantSettings settings,
        IDatabaseService db)
    {
        _providers = providers.ToList();
        _settings = settings;
        _db = db;

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

    public Task<string> ExplainTerminalOutputAsync(string output, CancellationToken ct = default)
        => _active.ExplainAsync(output, ct);

    public Task<string> SuggestCommandAsync(string description, string? shell = null, CancellationToken ct = default)
        => _active.SuggestCommandAsync(description, shell, ct);

    public IAsyncEnumerable<string> StreamChatAsync(
        IEnumerable<(string role, string content)> history,
        CancellationToken ct = default)
        => _active.ChatStreamAsync(history, ct);

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
            var model = type switch
            {
                AssistantProviderType.OpenAI => _settings.OpenAIModel,
                AssistantProviderType.Anthropic => _settings.AnthropicModel,
                _ => _settings.OllamaModel
            };
            _ = _db.SaveAssistantPreferencesAsync(found.ProviderName, model);
        }
    }

    public async Task RestorePreferencesAsync()
    {
        var prefs = await _db.GetAssistantPreferencesAsync();
        if (prefs is null) return;

        var providerType = prefs.Value.providerName switch
        {
            "OpenAI" => AssistantProviderType.OpenAI,
            "Anthropic" => AssistantProviderType.Anthropic,
            _ => AssistantProviderType.Ollama
        };

        var found = ResolveProvider(providerType);
        if (found is not null)
        {
            _active = found;
            _settings.ActiveProvider = providerType;

            switch (providerType)
            {
                case AssistantProviderType.Anthropic:
                    _settings.AnthropicModel = prefs.Value.model;
                    break;
                case AssistantProviderType.OpenAI:
                    _settings.OpenAIModel = prefs.Value.model;
                    break;
                default:
                    _settings.OllamaModel = prefs.Value.model;
                    break;
            }
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

    // ─── IDisposable ──────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        foreach (var provider in _providers)
        {
            try { (provider as IDisposable)?.Dispose(); } catch { }
        }
    }

    // ─── Settings bridge ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public void ApplySettings(string provider, string model, string baseUrl, string apiKey)
    {
        // Map the Settings-screen provider string to the enum
        var providerType = provider?.ToLowerInvariant() switch
        {
            "openai"    => AssistantProviderType.OpenAI,
            "anthropic" => AssistantProviderType.Anthropic,
            "local"     => AssistantProviderType.Ollama,
            "ollama"    => AssistantProviderType.Ollama,
            _           => AssistantProviderType.None
        };

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
                var modelName = providerType switch
                {
                    AssistantProviderType.OpenAI => _settings.OpenAIModel,
                    AssistantProviderType.Anthropic => _settings.AnthropicModel,
                    _ => _settings.OllamaModel
                };
                _ = _db.SaveAssistantPreferencesAsync(found.ProviderName, modelName);
            }
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────

    private IAssistantProvider? ResolveProvider(AssistantProviderType type)
    {
        // Map enum values to well-known provider names
        var targetName = type switch
        {
            AssistantProviderType.Ollama => "Ollama",
            AssistantProviderType.OpenAI => "OpenAI",
            AssistantProviderType.Anthropic => "Anthropic",
            _ => null
        };

        if (targetName is null)
            return null;

        // Try WSL-style name lookup first, then fall back to WIN ProviderName
        if (_providersByName.TryGetValue(targetName, out var found))
            return found;

        return _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, targetName, StringComparison.OrdinalIgnoreCase));
    }
}
