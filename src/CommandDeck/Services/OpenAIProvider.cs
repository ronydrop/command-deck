using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// AI assistant provider that communicates with OpenAI-compatible chat completion APIs.
/// Supports the official OpenAI API as well as any compatible endpoint (e.g., Azure OpenAI,
/// local proxies, or services that implement the /v1/chat/completions endpoint).
/// </summary>
public class OpenAIProvider : IAssistantProvider
{
    private readonly HttpClient _httpClient;
    private readonly ISecretStorageService? _secretStorage;
    private readonly ISettingsService? _settingsService;
    private readonly AssistantSettings _settings;

    private bool _isInitialized;
    private bool _isDisposed;
    private CancellationTokenSource? _currentCts;

    // Defaults
    private const string DefaultModel = "gpt-4o-mini";
    private const string DefaultBaseUrl = "https://api.openai.com/v1";
    private const string ApiKeySecretName = "ai_openai_api_key";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc/>
    public string ProviderName => "OpenAI";

    /// <inheritdoc/>
    public string Name => "openai";

    /// <inheritdoc/>
    public string DisplayName => "OpenAI";

    /// <inheritdoc/>
    public bool IsAvailable => true; // Always available (network-dependent at runtime)

    private string? _apiKey;
    private string _model = DefaultModel;
    private string _baseUrl = DefaultBaseUrl;

    /// <inheritdoc/>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey) || !string.IsNullOrWhiteSpace(_settings.OpenAIKey);

    /// <summary>
    /// Creates a new OpenAI provider.
    /// </summary>
    /// <param name="secretStorage">Secret storage for API key retrieval.</param>
    /// <param name="settingsService">Settings service for model/base URL configuration.</param>
    /// <param name="settings">Shared assistant settings for model/key overrides.</param>
    public OpenAIProvider(ISecretStorageService? secretStorage = null, ISettingsService? settingsService = null, AssistantSettings? settings = null)
    {
        _secretStorage = secretStorage;
        _settingsService = settingsService;
        _settings = settings ?? new AssistantSettings();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        // Load API key from secret storage
        // Note: model and base URL are NOT read from the shared AppSettings here because
        // AppSettings has a single AiModel/AiBaseUrl shared across all providers.
        // The correct values come through AssistantSettings (set by AssistantService.ApplySettings).
        if (_secretStorage != null)
        {
            try
            {
                _apiKey = await _secretStorage.RetrieveSecretAsync(ApiKeySecretName);
            }
            catch
            {
                _apiKey = null;
            }
        }

        // Configure HttpClient base address
        _httpClient.BaseAddress = new Uri(_baseUrl.TrimEnd('/') + "/");

        _isInitialized = true;
    }

    /// <inheritdoc/>
    public async Task<AssistantResponse> ChatAsync(IReadOnlyList<AssistantMessage> messages)
    {
        ThrowIfDisposed();

        if (!_isInitialized)
            await InitializeAsync();

        if (!IsConfigured)
            return AssistantResponse.Failed("OpenAI provider is not configured. Please set an API key in settings.");

        // Link to a CTS for cancellation support
        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        var token = _currentCts.Token;

        try
        {
            var requestBody = BuildRequestBody(messages);

            var apiKeyToUse = !string.IsNullOrWhiteSpace(_settings.OpenAIKey) ? _settings.OpenAIKey : _apiKey;

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKeyToUse);
            request.Content = JsonContent.Create(requestBody, options: JsonOptions);

            using var response = await _httpClient.SendAsync(request, token);
            var responseBody = await response.Content.ReadAsStringAsync(token);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = TryExtractErrorMessage(responseBody, response.StatusCode);
                return AssistantResponse.Failed(
                    $"OpenAI API error ({(int)response.StatusCode}): {errorDetail}");
            }

            return ParseResponse(responseBody);
        }
        catch (OperationCanceledException)
        {
            return AssistantResponse.Failed("Request was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return AssistantResponse.Failed($"Network error communicating with OpenAI: {ex.Message}");
        }
        catch (Exception ex)
        {
            return AssistantResponse.Failed($"Unexpected error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AssistantResponse> StreamChatAsync(
        IReadOnlyList<AssistantMessage> messages,
        Action<string>? onChunk = null)
    {
        // Streaming not yet implemented - returns single response as fallback
        await Task.Yield();
        yield return await ChatAsync(messages);
#pragma warning disable CS0162 // Unreachable code - will be implemented in future
        yield break;
#pragma warning restore CS0162
    }

    /// <inheritdoc/>
    public void CancelCurrentRequest()
    {
        _currentCts?.Cancel();
        _currentCts?.Dispose();
        _currentCts = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        CancelCurrentRequest();
        _httpClient.Dispose();
    }

    // ─── Legacy WIN interface adapters ────────────────────────────────────

    /// <inheritdoc/>
    public Task<string> ExplainAsync(string terminalOutput, CancellationToken ct = default)
    {
        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Explain the following terminal output in plain language and, if applicable, suggest how to fix any errors."),
            AssistantMessage.User($"Explain this terminal output:\n```\n{terminalOutput}\n```")
        };
        return ChatAsync(messages).ContinueWith(t =>
            t.IsFaulted ? t.Exception?.InnerException?.Message ?? "Error" :
            t.Result.IsError ? t.Result.Error : t.Result.Content ?? string.Empty, ct);
    }

    /// <inheritdoc/>
    public Task<string> SuggestCommandAsync(string description, string? shellHint = null, CancellationToken ct = default)
    {
        var shellPart = shellHint is not null ? $" The target shell is {shellHint}." : string.Empty;
        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Suggest a shell command. Reply with only the command and a brief one-sentence explanation."),
            AssistantMessage.User($"Suggest a shell command to accomplish: {description}{shellPart}")
        };
        return ChatAsync(messages).ContinueWith(t =>
            t.IsFaulted ? t.Exception?.InnerException?.Message ?? "Error" :
            t.Result.IsError ? t.Result.Error : t.Result.Content ?? string.Empty, ct);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        IEnumerable<(string role, string content)> history,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_isInitialized)
            await InitializeAsync();

        var apiKey = _apiKey;
        var model = !string.IsNullOrWhiteSpace(_settings?.OpenAIModel) ? _settings.OpenAIModel : _model;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return "Configure uma API key do OpenAI nas Configurações.";
            yield break;
        }

        if (string.IsNullOrWhiteSpace(model)) model = DefaultModel;

        var msgPayload = history.Select(h => new { role = h.role, content = h.content }).ToList();

        var requestBody = JsonSerializer.Serialize(new
        {
            model,
            messages = msgPayload,
            temperature = 0.7,
            max_tokens = 2048,
            stream = true
        }, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        string? networkError = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            networkError = $"Erro de rede com OpenAI: {ex.Message}";
        }

        if (networkError is not null || response is null)
        {
            yield return networkError ?? "Erro desconhecido ao conectar com OpenAI.";
            yield break;
        }

        using (response)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var data = line["data: ".Length..];
                if (data == "[DONE]") yield break;

                string? chunk = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.TryGetProperty("content", out var contentEl))
                        {
                            chunk = contentEl.GetString();
                        }
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed JSON lines from SSE stream
                }

                if (!string.IsNullOrEmpty(chunk))
                    yield return chunk;
            }
        }
    }

    // ─── Private: Build / Parse OpenAI API structures ─────────────────────

    /// <summary>
    /// Builds the chat completion request body.
    /// </summary>
    private object BuildRequestBody(IReadOnlyList<AssistantMessage> messages)
    {
        var modelToUse = !string.IsNullOrWhiteSpace(_settings.OpenAIModel) ? _settings.OpenAIModel : _model;

        return new
        {
            model = modelToUse,
            messages = messages.Select(m => new
            {
                role = m.Role.ToString().ToLowerInvariant(),
                content = m.Content
            }),
            temperature = 0.7,
            max_tokens = 2048
        };
    }

    /// <summary>
    /// Parses the OpenAI chat completion response into our model.
    /// </summary>
    private AssistantResponse ParseResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Extract content from the first choice
            var content = string.Empty;
            var finishReason = FinishReason.Unknown;

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentEl))
                {
                    content = contentEl.GetString() ?? string.Empty;
                }

                if (choice.TryGetProperty("finish_reason", out var finishEl))
                {
                    var reasonStr = finishEl.GetString() ?? "unknown";
                    finishReason = reasonStr switch
                    {
                        "stop" => FinishReason.Stop,
                        "length" => FinishReason.Length,
                        "tool_calls" => FinishReason.ToolCalls,
                        "error" or "cancelled" => FinishReason.Error,
                        _ => FinishReason.Unknown
                    };
                }
            }

            // Extract usage
            TokenUsage? usage = null;
            if (root.TryGetProperty("usage", out var usageEl))
            {
                usage = new TokenUsage
                {
                    PromptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                    CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ct)
                        ? ct.GetInt32() : 0
                };
            }

            return new AssistantResponse
            {
                Content = content,
                FinishReason = finishReason,
                Usage = usage
            };
        }
        catch (JsonException ex)
        {
            return AssistantResponse.Failed($"Failed to parse OpenAI response: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to extract a readable error message from an OpenAI error response.
    /// </summary>
    private static string TryExtractErrorMessage(string responseBody, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorEl))
            {
                if (errorEl.TryGetProperty("message", out var msgEl))
                    return msgEl.GetString() ?? responseBody;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenAIProvider] Failed to parse error body as JSON: {ex.Message}");
        }

        return responseBody.Length > 200 ? responseBody[..200] + "..." : responseBody;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
