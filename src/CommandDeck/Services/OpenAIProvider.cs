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
    public string DisplayName => "GPT";
    public string DisplayColor => "#A6E3A1";  // Catppuccin green

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
        ThrowIfDisposed();

        if (!_isInitialized)
            await InitializeAsync();

        if (!IsConfigured)
        {
            yield return AssistantResponse.Failed("OpenAI provider is not configured. Please set an API key in settings.");
            yield break;
        }

        // Link to a CTS for cancellation support
        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        var ct = _currentCts.Token;

        var apiKeyToUse = !string.IsNullOrWhiteSpace(_settings.OpenAIKey) ? _settings.OpenAIKey : _apiKey;

        // Build request body with stream = true
        var modelToUse = !string.IsNullOrWhiteSpace(_settings.OpenAIModel) ? _settings.OpenAIModel : _model;
        var requestBody = JsonSerializer.Serialize(new
        {
            model = modelToUse,
            messages = messages.Select(m => new
            {
                role = m.Role.ToString().ToLowerInvariant(),
                content = m.Content
            }),
            temperature = 0.7,
            max_tokens = 2048,
            stream = true
        }, JsonOptions);

        HttpResponseMessage? response = null;
        string? networkError = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKeyToUse);
            request.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                var detail = TryExtractErrorMessage(errorBody, response.StatusCode);
                networkError = $"OpenAI API error ({(int)response.StatusCode}): {detail}";
            }
        }
        catch (OperationCanceledException)
        {
            networkError = "Request was cancelled.";
        }
        catch (HttpRequestException ex)
        {
            networkError = $"Network error communicating with OpenAI: {ex.Message}";
        }

        if (networkError is not null || response is null)
        {
            yield return AssistantResponse.Failed(networkError ?? "Unknown error connecting to OpenAI.");
            yield break;
        }

        // Parse SSE stream
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
                {
                    onChunk?.Invoke(chunk);
                    yield return AssistantResponse.Success(chunk);
                }
            }
        }
    }

    /// <summary>
    /// Streaming chat with optional tool/function calling support (OpenAI format).
    /// </summary>
    public async IAsyncEnumerable<AssistantResponse> StreamChatAsync(
        IReadOnlyList<AssistantMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ThrowIfDisposed();

        if (!_isInitialized)
            await InitializeAsync();

        if (!IsConfigured)
        {
            yield return AssistantResponse.Failed("OpenAI provider is not configured. Please set an API key in settings.");
            yield break;
        }

        var apiKeyToUse = !string.IsNullOrWhiteSpace(_settings.OpenAIKey) ? _settings.OpenAIKey : _apiKey;
        var modelToUse  = !string.IsNullOrWhiteSpace(_settings.OpenAIModel) ? _settings.OpenAIModel : _model;

        var serializedMsgs = SerializeMessages(messages);
        var bodyDict = new Dictionary<string, object>
        {
            ["model"]       = modelToUse,
            ["messages"]    = serializedMsgs,
            ["temperature"] = 0.7,
            ["max_tokens"]  = 4096,
            ["stream"]      = true
        };

        if (tools is { Count: > 0 })
        {
            bodyDict["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name        = t.Name,
                    description = t.Description,
                    parameters  = t.InputSchema
                }
            }).ToArray();
            bodyDict["tool_choice"] = "auto";
        }

        var requestJson = JsonSerializer.Serialize(bodyDict, JsonOptions);

        HttpResponseMessage? response = null;
        string? networkError = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKeyToUse);
            request.Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                networkError = $"OpenAI API error ({(int)response.StatusCode}): {TryExtractErrorMessage(errorBody, response.StatusCode)}";
            }
        }
        catch (OperationCanceledException) { networkError = "Request was cancelled."; }
        catch (HttpRequestException ex)    { networkError = $"Network error: {ex.Message}"; }

        if (networkError is not null || response is null)
        {
            yield return AssistantResponse.Failed(networkError ?? "Unknown error connecting to OpenAI.");
            yield break;
        }

        // Accumulators for streamed tool-call deltas: index → (id, name, arguments builder)
        var toolAcc = new Dictionary<int, (string Id, string Name, System.Text.StringBuilder Args)>();
        var textSb  = new System.Text.StringBuilder();
        var finalFinish = FinishReason.Stop;

        using (response)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

                var data = line["data: ".Length..];
                if (data == "[DONE]") break;

                AssistantResponse? pending = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                    var choice = choices[0];

                    // Capture finish_reason when present (non-null)
                    if (choice.TryGetProperty("finish_reason", out var finishEl) && finishEl.ValueKind == JsonValueKind.String)
                    {
                        finalFinish = finishEl.GetString() switch
                        {
                            "tool_calls" => FinishReason.ToolCalls,
                            "length"     => FinishReason.Length,
                            _            => FinishReason.Stop
                        };
                    }

                    if (!choice.TryGetProperty("delta", out var delta)) continue;

                    // Text token
                    if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                    {
                        var text = contentEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            textSb.Append(text);
                            pending = AssistantResponse.Success(text);
                        }
                    }

                    // Tool call deltas
                    if (delta.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tcDelta in tcArr.EnumerateArray())
                        {
                            var idx = tcDelta.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;

                            if (!toolAcc.ContainsKey(idx))
                            {
                                var id   = tcDelta.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                                var name = tcDelta.TryGetProperty("function", out var fn0) && fn0.TryGetProperty("name", out var nm0)
                                    ? nm0.GetString() ?? string.Empty : string.Empty;
                                toolAcc[idx] = (id, name, new System.Text.StringBuilder());
                            }

                            // Append arguments fragment (StringBuilder is a reference — safe to mutate via copy)
                            if (tcDelta.TryGetProperty("function", out var fnEl) && fnEl.TryGetProperty("arguments", out var argsEl))
                            {
                                var fragment = argsEl.GetString();
                                if (!string.IsNullOrEmpty(fragment))
                                    toolAcc[idx].Args.Append(fragment);
                            }
                        }
                    }
                }
                catch (JsonException) { /* skip malformed chunks */ }

                if (pending is not null) yield return pending;
            }
        }

        // Emit a final response carrying the accumulated tool calls
        if (toolAcc.Count > 0)
        {
            var calls = toolAcc
                .OrderBy(kv => kv.Key)
                .Select(kv => new ToolCall { Id = kv.Value.Id, Name = kv.Value.Name, InputJson = kv.Value.Args.ToString() })
                .ToList();
            yield return AssistantResponse.WithToolCalls(textSb.ToString(), calls);
        }
        else if (finalFinish != FinishReason.Stop)
        {
            yield return new AssistantResponse { FinishReason = finalFinish };
        }
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
            t.Result.IsError ? t.Result.Error ?? string.Empty : t.Result.Content ?? string.Empty, ct);
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
            t.Result.IsError ? t.Result.Error ?? string.Empty : t.Result.Content ?? string.Empty, ct);
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
            messages = SerializeMessages(messages),
            temperature = 0.7,
            max_tokens = 2048
        };
    }

    /// <summary>
    /// Converts AssistantMessages to OpenAI-format objects, handling the
    /// tool and assistant-with-tool-calls roles.
    /// </summary>
    private static List<object> SerializeMessages(IReadOnlyList<AssistantMessage> messages)
    {
        var result = new List<object>(messages.Count);
        foreach (var m in messages)
        {
            if (m.Role == AssistantRole.Tool)
            {
                result.Add(new
                {
                    role         = "tool",
                    tool_call_id = m.ToolCallId ?? string.Empty,
                    content      = m.Content
                });
            }
            else if (m.Role == AssistantRole.Assistant && m.ToolCalls is { Count: > 0 })
            {
                result.Add(new
                {
                    role       = "assistant",
                    content    = string.IsNullOrEmpty(m.Content) ? null : m.Content,
                    tool_calls = m.ToolCalls.Select(tc => new
                    {
                        id       = tc.Id,
                        type     = "function",
                        function = new { name = tc.Name, arguments = tc.InputJson }
                    }).ToArray()
                });
            }
            else
            {
                result.Add(new
                {
                    role    = m.Role.ToString().ToLowerInvariant(),
                    content = m.Content
                });
            }
        }
        return result;
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

            var content = string.Empty;
            var finishReason = FinishReason.Unknown;
            var toolCalls = new List<ToolCall>();

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];

                if (choice.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                        content = contentEl.GetString() ?? string.Empty;

                    // Extract tool_calls from non-streaming response
                    if (message.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcArr.EnumerateArray())
                        {
                            var id   = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                            var name = tc.TryGetProperty("function", out var fnEl) && fnEl.TryGetProperty("name", out var nameEl)
                                ? nameEl.GetString() ?? string.Empty : string.Empty;
                            var args = fnEl.ValueKind == JsonValueKind.Object && fnEl.TryGetProperty("arguments", out var argsEl)
                                ? argsEl.GetString() ?? "{}" : "{}";
                            toolCalls.Add(new ToolCall { Id = id, Name = name, InputJson = args });
                        }
                    }
                }

                if (choice.TryGetProperty("finish_reason", out var finishEl))
                {
                    finishReason = finishEl.GetString() switch
                    {
                        "stop"       => FinishReason.Stop,
                        "length"     => FinishReason.Length,
                        "tool_calls" => FinishReason.ToolCalls,
                        "error" or "cancelled" => FinishReason.Error,
                        _ => FinishReason.Unknown
                    };
                }
            }

            TokenUsage? usage = null;
            if (root.TryGetProperty("usage", out var usageEl))
            {
                usage = new TokenUsage
                {
                    PromptTokens     = usageEl.TryGetProperty("prompt_tokens",     out var pt) ? pt.GetInt32() : 0,
                    CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0
                };
            }

            return toolCalls.Count > 0
                ? AssistantResponse.WithToolCalls(content, toolCalls)
                : new AssistantResponse { Content = content, FinishReason = finishReason, Usage = usage };
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
