using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Facade over multiple IAssistantProvider implementations.
/// Allows runtime switching and hides provider details from consumers.
/// </summary>
public interface IAssistantService : IDisposable
{
    /// <summary>The currently active backend provider.</summary>
    IAssistantProvider ActiveProvider { get; }

    /// <summary>True when at least one registered provider reports IsAvailable.</summary>
    bool IsAnyProviderAvailable { get; }

    /// <summary>Ask the active provider to explain terminal output.</summary>
    Task<string> ExplainTerminalOutputAsync(string output, CancellationToken ct = default);

    /// <summary>Ask the active provider to suggest a shell command.</summary>
    Task<string> SuggestCommandAsync(string description, string? shell = null, CancellationToken ct = default);

    /// <summary>Stream a chat response from the active provider.</summary>
    IAsyncEnumerable<string> StreamChatAsync(IEnumerable<(string role, string content)> history, CancellationToken ct = default);

    /// <summary>Switch the active provider at runtime.</summary>
    void SwitchProvider(AssistantProviderType type);

    /// <summary>Restaura as preferencias de provider salvas no banco de dados.</summary>
    Task RestorePreferencesAsync();

    // ─── Expanded WSL members ─────────────────────────────────────────────

    /// <summary>
    /// Gets all registered providers.
    /// </summary>
    IReadOnlyList<IAssistantProvider> GetProviders();

    /// <summary>
    /// Sets the active provider by its unique name.
    /// </summary>
    Task SetActiveProviderAsync(string providerName);

    /// <summary>
    /// Gets the currently active provider, or null if none is set.
    /// </summary>
    IAssistantProvider? GetActiveProvider();

    /// <summary>
    /// Gets the display name of the active provider, or "None" if not set.
    /// </summary>
    string ActiveProviderDisplayName { get; }

    /// <summary>
    /// Registers a new provider. If a provider with the same name exists, it is replaced.
    /// </summary>
    void RegisterProvider(IAssistantProvider provider);

    /// <summary>
    /// Uses the active AI provider to explain terminal output (simplified overload).
    /// </summary>
    Task<string> ExplainTerminalOutputAsync(string output);

    /// <summary>
    /// Uses the active AI provider to suggest commands based on terminal context.
    /// Returns a list of suggested command strings.
    /// </summary>
    Task<List<string>> SuggestCommandsAsync(string context);

    /// <summary>
    /// Event raised when an assistant response is received.
    /// </summary>
    event Action<AssistantResponse>? AssistantResponseReceived;

    /// <summary>
    /// Applies AI settings from the Settings screen to the assistant provider configuration.
    /// Maps provider string ("openai"/"local"/"none") to the correct provider type and updates model/url/key.
    /// </summary>
    void ApplySettings(string provider, string model, string baseUrl, string apiKey, string? anthropicAuthMode = null);

    /// <summary>
    /// Initializes the service and all registered providers.
    /// </summary>
    Task InitializeAsync();
}
