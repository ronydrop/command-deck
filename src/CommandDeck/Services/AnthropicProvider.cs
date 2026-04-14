using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// AI assistant provider that communicates with the Anthropic Messages API.
/// Supports streaming via SSE (server-sent events).
/// </summary>
public sealed class AnthropicProvider : IAssistantProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ISecretStorageService? _secretStorage;
    private readonly ISettingsService? _settingsService;
    private readonly IClaudeOAuthService? _oauthService;
    private readonly AssistantSettings _settings;

    private bool _isInitialized;
    private bool _isDisposed;
    private CancellationTokenSource? _currentCts;

    private const string DefaultModel = "claude-haiku-4-5-20251001";
    private const string DefaultBaseUrl = "https://api.anthropic.com";
    private const string ApiVersion = "2023-06-01";
    private const string OAuthBetaHeader = "oauth-2025-04-20";
    private const string ApiKeySecretName = "ai_anthropic_api_key";

    private string? _apiKey;
    private string _model = DefaultModel;
    private string _baseUrl = DefaultBaseUrl;

    public string ProviderName => "Anthropic";
    public string Name => "anthropic";
    public string DisplayName => "Claude";
    public string DisplayColor => "#CBA6F7";  // Catppuccin mauve
    public bool IsAvailable => true;

    public bool IsConfigured =>
        _settings.AnthropicAuth == AnthropicAuthMode.ClaudeOAuth
            ? (_oauthService?.IsClaudeCodeInstalled ?? false)
            : (!string.IsNullOrWhiteSpace(_apiKey) || !string.IsNullOrWhiteSpace(_settings.AnthropicKey));

    private bool IsOAuthMode => _settings.AnthropicAuth == AnthropicAuthMode.ClaudeOAuth;

    public AnthropicProvider(
        ISecretStorageService? secretStorage = null,
        ISettingsService? settingsService = null,
        AssistantSettings? settings = null,
        IClaudeOAuthService? oauthService = null)
    {
        _secretStorage = secretStorage;
        _settingsService = settingsService;
        _settings = settings ?? new AssistantSettings();
        _oauthService = oauthService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        // Load API key from secret storage only.
        // Model and base URL are NOT read from the shared AppSettings here because
        // AppSettings has a single AiModel/AiBaseUrl shared across all providers,
        // which would contaminate this provider with values intended for others.
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

        _isInitialized = true;
    }

    public async Task<AssistantResponse> ChatAsync(IReadOnlyList<AssistantMessage> messages)
    {
        ThrowIfDisposed();

        if (!_isInitialized)
            await InitializeAsync();

        if (!IsConfigured)
        {
            var hint = IsOAuthMode
                ? "Claude Code não encontrado. Instale o Claude Code ou use uma API key."
                : "Anthropic provider não configurado. Defina uma API key nas Configurações.";
            return AssistantResponse.Failed(hint);
        }

        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        var token = _currentCts.Token;

        try
        {
            var modelToUse = !string.IsNullOrWhiteSpace(_settings.AnthropicModel) ? _settings.AnthropicModel : _model;

            var (systemPrompt, userMessages) = SplitMessages(messages);
            var requestBody = BuildRequestBody(modelToUse, systemPrompt, userMessages, stream: false);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl.TrimEnd('/')}/v1/messages");
            await ConfigureRequestAuthAsync(request);
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, token);
            var responseBody = await response.Content.ReadAsStringAsync(token);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = TryExtractErrorMessage(responseBody);
                return AssistantResponse.Failed(
                    $"Anthropic API error ({(int)response.StatusCode}): {errorDetail}");
            }

            return ParseResponse(responseBody);
        }
        catch (OperationCanceledException)
        {
            return AssistantResponse.Failed("Requisição cancelada.");
        }
        catch (HttpRequestException ex)
        {
            return AssistantResponse.Failed($"Erro de rede com Anthropic ({_baseUrl}/v1/messages): {ex.Message}");
        }
        catch (Exception ex)
        {
            return AssistantResponse.Failed($"Erro inesperado: {ex.Message}");
        }
    }

    public async IAsyncEnumerable<AssistantResponse> StreamChatAsync(
        IReadOnlyList<AssistantMessage> messages,
        Action<string>? onChunk = null)
    {
        await foreach (var resp in StreamChatAsync(messages, tools: null, CancellationToken.None))
        {
            if (!resp.IsError && !string.IsNullOrEmpty(resp.Content))
                onChunk?.Invoke(resp.Content);
            yield return resp;
        }
    }

    public async IAsyncEnumerable<AssistantResponse> StreamChatAsync(
        IReadOnlyList<AssistantMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken ct)
    {
        ThrowIfDisposed();

        if (!_isInitialized)
            await InitializeAsync();

        if (!IsConfigured)
        {
            var hint = IsOAuthMode
                ? "Claude Code não encontrado. Instale o Claude Code ou use uma API key."
                : "Anthropic provider não configurado. Defina uma API key nas Configurações.";
            yield return AssistantResponse.Failed(hint);
            yield break;
        }

        var modelToUse = !string.IsNullOrWhiteSpace(_settings.AnthropicModel) ? _settings.AnthropicModel : _model;
        var (systemPrompt, userMessages) = SplitMessages(messages);
        var requestBody = BuildRequestBody(modelToUse, systemPrompt, userMessages, stream: true, tools);

        HttpResponseMessage? response = null;
        string? networkError = null;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl.TrimEnd('/')}/v1/messages");
            await ConfigureRequestAuthAsync(request);
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                var detail = TryExtractErrorMessage(errorBody);
                networkError = $"Anthropic API error ({(int)response.StatusCode}): {detail}";
            }
        }
        catch (OperationCanceledException) { yield break; }
        catch (HttpRequestException ex)
        {
            networkError = $"Erro de rede com Anthropic ({_baseUrl}/v1/messages): {ex.Message}";
        }

        if (networkError is not null || response is null)
        {
            yield return AssistantResponse.Failed(networkError ?? "Erro desconhecido ao conectar com Anthropic.");
            yield break;
        }

        using (response)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            // State for accumulating tool_use blocks across SSE events
            // Key = content_block index, Value = (tool_use_id, tool_name, accumulated_input_json)
            var toolAccumulators = new Dictionary<int, (string Id, string Name, System.Text.StringBuilder Input)>();
            var finalFinishReason = FinishReason.Stop;
            bool toolsRequested = false;

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var data = line["data: ".Length..];
                if (data == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("type", out var typeEl)) continue;
                    var eventType = typeEl.GetString();

                    switch (eventType)
                    {
                        case "content_block_start":
                        {
                            // Check if this is a tool_use block start
                            if (root.TryGetProperty("index", out var idxEl) &&
                                root.TryGetProperty("content_block", out var block) &&
                                block.TryGetProperty("type", out var bType) &&
                                bType.GetString() == "tool_use")
                            {
                                var idx    = idxEl.GetInt32();
                                var toolId = block.TryGetProperty("id",   out var idEl)   ? (idEl.GetString()   ?? string.Empty) : string.Empty;
                                var toolNm = block.TryGetProperty("name", out var nmEl)   ? (nmEl.GetString()   ?? string.Empty) : string.Empty;
                                toolAccumulators[idx] = (toolId, toolNm, new System.Text.StringBuilder());
                            }
                            break;
                        }

                        case "content_block_delta":
                        {
                            if (!root.TryGetProperty("delta", out var delta)) break;
                            var deltaType = delta.TryGetProperty("type", out var dt) ? dt.GetString() : null;

                            if (deltaType == "text_delta" && delta.TryGetProperty("text", out var textEl))
                            {
                                var chunk = textEl.GetString();
                                if (!string.IsNullOrEmpty(chunk))
                                    yield return AssistantResponse.Success(chunk);
                            }
                            else if (deltaType == "input_json_delta" &&
                                     root.TryGetProperty("index", out var idxEl) &&
                                     delta.TryGetProperty("partial_json", out var pjEl))
                            {
                                var idx = idxEl.GetInt32();
                                if (toolAccumulators.TryGetValue(idx, out var acc))
                                    acc.Input.Append(pjEl.GetString());
                            }
                            break;
                        }

                        case "message_delta":
                        {
                            if (root.TryGetProperty("delta", out var delta) &&
                                delta.TryGetProperty("stop_reason", out var sr) &&
                                sr.GetString() == "tool_use")
                            {
                                finalFinishReason = FinishReason.ToolCalls;
                                toolsRequested = true;
                            }
                            break;
                        }

                        case "message_stop":
                            goto done;
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed SSE lines
                }
            }

            done:
            // Emit accumulated tool calls as a final response chunk
            if (toolsRequested && toolAccumulators.Count > 0)
            {
                var calls = toolAccumulators.Values
                    .Select(acc => new ToolCall
                    {
                        Id        = acc.Id,
                        Name      = acc.Name,
                        InputJson = acc.Input.ToString()
                    })
                    .ToList();

                yield return AssistantResponse.WithToolCalls(string.Empty, calls);
            }
        }
    }

    public async Task<string> ExplainAsync(string terminalOutput, CancellationToken ct = default)
    {
        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Explain the following terminal output in plain language and, if applicable, suggest how to fix any errors."),
            AssistantMessage.User($"Explain this terminal output:\n```\n{terminalOutput}\n```")
        };
        var result = await ChatAsync(messages);
        return result.IsError ? result.Error ?? string.Empty : result.Content ?? string.Empty;
    }

    public async Task<string> SuggestCommandAsync(string description, string? shellHint = null, CancellationToken ct = default)
    {
        var shellPart = shellHint is not null ? $" The target shell is {shellHint}." : string.Empty;
        var messages = new List<AssistantMessage>
        {
            AssistantMessage.System("You are a helpful developer assistant. Suggest a shell command. Reply with only the command and a brief one-sentence explanation."),
            AssistantMessage.User($"Suggest a shell command to accomplish: {description}{shellPart}")
        };
        var result = await ChatAsync(messages);
        return result.IsError ? result.Error ?? string.Empty : result.Content ?? string.Empty;
    }

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

    // ─── Auth configuration ────────────────────────────────────────────────

    private async Task ConfigureRequestAuthAsync(HttpRequestMessage request)
    {
        if (IsOAuthMode)
        {
            var oauthToken = await (_oauthService?.GetValidAccessTokenAsync() ?? Task.FromResult<string?>(null));
            if (string.IsNullOrEmpty(oauthToken))
                throw new InvalidOperationException("Falha ao obter token OAuth do Claude Code.");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken);
            request.Headers.Add("anthropic-beta", OAuthBetaHeader);
        }
        else
        {
            var apiKeyToUse = !string.IsNullOrWhiteSpace(_settings.AnthropicKey) ? _settings.AnthropicKey : _apiKey;
            request.Headers.Add("x-api-key", apiKeyToUse);
        }

        request.Headers.Add("anthropic-version", ApiVersion);
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private static (string? system, List<AssistantMessage> messages) SplitMessages(IReadOnlyList<AssistantMessage> messages)
    {
        string? system = null;
        var result = new List<AssistantMessage>();

        foreach (var msg in messages)
        {
            if (msg.Role == AssistantRole.System)
                system = msg.Content;
            else
                result.Add(msg);
        }

        return (system, result);
    }

    private static string BuildRequestBody(
        string model,
        string? system,
        List<AssistantMessage> messages,
        bool stream,
        IReadOnlyList<ToolDefinition>? tools = null,
        int maxTokens = 8192)
    {
        var bodyObj = new Dictionary<string, object>
        {
            ["model"]      = model,
            ["max_tokens"] = maxTokens,
            ["stream"]     = stream,
            ["messages"]   = SerializeMessages(messages)
        };

        if (!string.IsNullOrWhiteSpace(system))
            bodyObj["system"] = system;

        if (tools is { Count: > 0 })
        {
            bodyObj["tools"] = tools.Select(t => new Dictionary<string, object>
            {
                ["name"]         = t.Name,
                ["description"]  = t.Description,
                ["input_schema"] = t.InputSchema
            }).ToList();
        }

        return JsonSerializer.Serialize(bodyObj);
    }

    // Serialize messages including Tool-role (tool_result) and AssistantWithTools turns.
    private static List<object> SerializeMessages(List<AssistantMessage> messages)
    {
        var result = new List<object>();
        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case AssistantRole.User:
                    result.Add(new { role = "user", content = msg.Content });
                    break;

                case AssistantRole.Assistant when msg.ToolCalls is { Count: > 0 }:
                    // Assistant turn that requested tool use — Anthropic expects content blocks
                    var contentBlocks = new List<object>();
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                        contentBlocks.Add(new { type = "text", text = msg.Content });
                    foreach (var tc in msg.ToolCalls)
                    {
                        var inputObj = string.IsNullOrWhiteSpace(tc.InputJson)
                            ? (object)new { }
                            : JsonSerializer.Deserialize<JsonElement>(tc.InputJson);
                        contentBlocks.Add(new { type = "tool_use", id = tc.Id, name = tc.Name, input = inputObj });
                    }
                    result.Add(new { role = "assistant", content = contentBlocks });
                    break;

                case AssistantRole.Assistant:
                    result.Add(new { role = "assistant", content = msg.Content });
                    break;

                case AssistantRole.Tool:
                    // Tool results go as a user message with a tool_result content block
                    result.Add(new
                    {
                        role = "user",
                        content = new[]
                        {
                            new
                            {
                                type = "tool_result",
                                tool_use_id = msg.ToolCallId ?? string.Empty,
                                content = msg.Content
                            }
                        }
                    });
                    break;

                default:
                    result.Add(new { role = "user", content = msg.Content });
                    break;
            }
        }
        return result;
    }

    private static AssistantResponse ParseResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var textContent = string.Empty;
            var toolCalls = new List<ToolCall>();

            if (root.TryGetProperty("content", out var contentArray))
            {
                foreach (var block in contentArray.EnumerateArray())
                {
                    if (!block.TryGetProperty("type", out var blockType)) continue;

                    var bType = blockType.GetString();
                    if (bType == "text" && block.TryGetProperty("text", out var textEl))
                    {
                        textContent += textEl.GetString() ?? string.Empty;
                    }
                    else if (bType == "tool_use")
                    {
                        var id   = block.TryGetProperty("id",   out var idEl)   ? (idEl.GetString()   ?? string.Empty) : string.Empty;
                        var name = block.TryGetProperty("name", out var nmEl)   ? (nmEl.GetString()   ?? string.Empty) : string.Empty;
                        var inputJson = block.TryGetProperty("input", out var inEl)
                            ? inEl.GetRawText()
                            : "{}";
                        toolCalls.Add(new ToolCall { Id = id, Name = name, InputJson = inputJson });
                    }
                }
            }

            var finishReason = FinishReason.Unknown;
            if (root.TryGetProperty("stop_reason", out var stopEl))
            {
                finishReason = (stopEl.GetString() ?? "") switch
                {
                    "end_turn"   => FinishReason.Stop,
                    "max_tokens" => FinishReason.Length,
                    "tool_use"   => FinishReason.ToolCalls,
                    _            => FinishReason.Unknown
                };
            }

            TokenUsage? usage = null;
            if (root.TryGetProperty("usage", out var usageEl))
            {
                usage = new TokenUsage
                {
                    PromptTokens     = usageEl.TryGetProperty("input_tokens",  out var pt) ? pt.GetInt32() : 0,
                    CompletionTokens = usageEl.TryGetProperty("output_tokens", out var ct) ? ct.GetInt32() : 0
                };
            }

            return new AssistantResponse
            {
                Content      = textContent,
                FinishReason = finishReason,
                ToolCalls    = toolCalls,
                Usage        = usage
            };
        }
        catch (JsonException ex)
        {
            return AssistantResponse.Failed($"Failed to parse Anthropic response: {ex.Message}");
        }
    }

    private static string TryExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorEl) &&
                errorEl.TryGetProperty("message", out var msgEl))
                return msgEl.GetString() ?? responseBody;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AnthropicProvider] Failed to parse error body as JSON: {ex.Message}");
        }

        return responseBody.Length > 200 ? responseBody[..200] + "..." : responseBody;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
