using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// IAssistantProvider implementation that talks to a local Ollama instance via /api/chat.
/// </summary>
public sealed class OllamaProvider : IAssistantProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly AssistantSettings _settings;
    private CancellationTokenSource? _currentCts;

    // ─── Availability cache (avoids deadlock from sync-over-async) ───────
    private bool _isAvailableCache;
    private DateTime _lastAvailabilityCheck = DateTime.MinValue;
    private static readonly TimeSpan AvailabilityCacheDuration = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a new Ollama provider with its own HttpClient (180s timeout for local LLMs).
    /// </summary>
    /// <param name="settings">Shared assistant settings for base URL and model configuration.</param>
    public OllamaProvider(AssistantSettings settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
    }

    public string ProviderName => "Ollama";
    public string DisplayName => "Ollama";
    public string DisplayColor => "#FAB387";  // Catppuccin peach

    /// <summary>
    /// Returns cached availability status. Call <see cref="CheckAvailabilityAsync"/> to refresh.
    /// Never blocks the UI thread.
    /// </summary>
    public bool IsAvailable => _isAvailableCache;

    /// <summary>
    /// Performs an async HTTP check against <c>/api/tags</c> and caches the result for 10 seconds.
    /// </summary>
    public async Task<bool> CheckAvailabilityAsync()
    {
        if (DateTime.UtcNow - _lastAvailabilityCheck < AvailabilityCacheDuration)
            return _isAvailableCache;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var url = $"{_settings.OllamaBaseUrl.TrimEnd('/')}/api/tags";
            var response = await _http.GetAsync(url, cts.Token);
            _isAvailableCache = response.IsSuccessStatusCode;
        }
        catch
        {
            _isAvailableCache = false;
        }

        _lastAvailabilityCheck = DateTime.UtcNow;
        return _isAvailableCache;
    }

    /// <summary>
    /// Sends terminal output to the model and requests a plain-language explanation.
    /// </summary>
    public async Task<string> ExplainAsync(string terminalOutput, CancellationToken ct = default)
    {
        var prompt =
            "You are a helpful developer assistant. " +
            "Explain the following terminal output in plain language and, if applicable, suggest how to fix any errors.\n\n" +
            $"Terminal output:\n```\n{terminalOutput}\n```";

        return await SendSingleMessageAsync(prompt, ct);
    }

    /// <summary>
    /// Asks the model to suggest a shell command for the given description.
    /// </summary>
    public async Task<string> SuggestCommandAsync(string description, string? shellHint = null, CancellationToken ct = default)
    {
        var shellPart = shellHint is not null ? $" The target shell is {shellHint}." : string.Empty;
        var prompt =
            "You are a helpful developer assistant. " +
            $"Suggest a shell command to accomplish the following task.{shellPart} " +
            "Reply with only the command and a brief one-sentence explanation.\n\n" +
            $"Task: {description}";

        return await SendSingleMessageAsync(prompt, ct);
    }

    // ─── AssistantMessage API ─────────────────────────────────────────────────

    /// <summary>
    /// Non-streaming chat completion using the <see cref="AssistantMessage"/> API.
    /// Maps <see cref="AssistantRole"/> to Ollama role strings.
    /// </summary>
    public async Task<AssistantResponse> ChatAsync(IReadOnlyList<AssistantMessage> messages)
    {
        var ollamaMessages = MapToOllamaMessages(messages);
        var modelToUse = ResolveModel();

        var request = new OllamaRequest
        {
            Model = modelToUse,
            Stream = false,
            Messages = ollamaMessages
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var url = $"{_settings.OllamaBaseUrl.TrimEnd('/')}/api/chat";

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var response = await _http.SendAsync(httpRequest);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return AssistantResponse.Failed($"Erro {(int)response.StatusCode} do Ollama: {errorBody.Trim()}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OllamaResponse>(responseJson, _jsonOptions);

            return AssistantResponse.Success(result?.Content ?? string.Empty);
        }
        catch (Exception ex)
        {
            return AssistantResponse.Failed($"Ollama request failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Streaming chat completion using the <see cref="AssistantMessage"/> API.
    /// Yields partial <see cref="AssistantResponse"/> chunks as they arrive.
    /// </summary>
    public async IAsyncEnumerable<AssistantResponse> StreamChatAsync(
        IReadOnlyList<AssistantMessage> messages,
        Action<string>? onChunk = null)
    {
        var ollamaMessages = MapToOllamaMessages(messages);

        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        var ct = _currentCts.Token;

        await foreach (var token in StreamOllamaAsync(ollamaMessages, ct))
        {
            onChunk?.Invoke(token);
            yield return AssistantResponse.Success(token);
        }
    }

    // ─── Tool-use (best-effort, model allowlist) ──────────────────────────────

    /// <summary>
    /// Models that support Ollama function-calling (/api/chat tools field).
    /// Models outside this list receive <c>tools = null</c> — falls back to text-only.
    /// </summary>
    private static readonly HashSet<string> _toolCapableModelPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "llama3.1", "llama3.2", "llama3.3", "mistral", "qwen2", "qwen2.5", "hermes", "firefunction"
    };

    private bool ModelSupportsTools(string model)
        => _toolCapableModelPrefixes.Any(prefix => model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Streaming chat with optional tool/function calling.
    /// Tools are only forwarded to models that support Ollama's native function calling.
    /// </summary>
    public async IAsyncEnumerable<AssistantResponse> StreamChatAsync(
        IReadOnlyList<AssistantMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var model = ResolveModel();
        var includeTools = tools is { Count: > 0 } && ModelSupportsTools(model);

        // Build request body as a raw dict so we can optionally include tools
        var ollamaMsgs = MapToOllamaMessagesWithTools(messages);
        var bodyDict = new Dictionary<string, object>
        {
            ["model"]    = model,
            ["stream"]   = true,
            ["messages"] = ollamaMsgs
        };

        if (includeTools)
        {
            bodyDict["tools"] = tools!.Select(t => new
            {
                type     = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.InputSchema }
            }).ToArray();
        }

        var json = JsonSerializer.Serialize(bodyDict, _jsonOptions);
        var url  = $"{_settings.OllamaBaseUrl.TrimEnd('/')}/api/chat";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? response = null;
        try
        {
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            yield return AssistantResponse.Failed($"Ollama request failed: {ex.Message}");
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            yield return AssistantResponse.Failed($"Erro {(int)response.StatusCode} do Ollama: {errorBody.Trim()}");
            response.Dispose();
            yield break;
        }

        using (response)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            var finalChunkJson = string.Empty;

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc   = JsonDocument.Parse(line);
                    var root        = doc.RootElement;
                    var done        = root.TryGetProperty("done", out var doneEl) && doneEl.GetBoolean();
                    var contentText = root.TryGetProperty("message", out var msgEl) && msgEl.TryGetProperty("content", out var cEl)
                        ? cEl.GetString() : null;

                    if (!string.IsNullOrEmpty(contentText))
                        yield return AssistantResponse.Success(contentText);

                    if (done)
                    {
                        finalChunkJson = line;
                        break;
                    }
                }
                catch (JsonException) { /* skip malformed lines */ }
            }

            // Parse tool_calls from the final done=true chunk
            if (includeTools && !string.IsNullOrEmpty(finalChunkJson))
            {
                var toolCalls = ParseOllamaToolCalls(finalChunkJson);
                if (toolCalls.Count > 0)
                    yield return AssistantResponse.WithToolCalls(string.Empty, toolCalls);
            }
        }
    }

    private static List<ToolCall> ParseOllamaToolCalls(string finalJson)
    {
        var result = new List<ToolCall>();
        try
        {
            using var doc = JsonDocument.Parse(finalJson);
            if (!doc.RootElement.TryGetProperty("message", out var msg)) return result;
            if (!msg.TryGetProperty("tool_calls", out var tcArr) || tcArr.ValueKind != JsonValueKind.Array) return result;

            var idx = 0;
            foreach (var tc in tcArr.EnumerateArray())
            {
                if (!tc.TryGetProperty("function", out var fn)) continue;
                var name = fn.TryGetProperty("name", out var nmEl) ? nmEl.GetString() ?? string.Empty : string.Empty;
                // Ollama delivers arguments as a JSON object; serialize it back to string
                var argsStr = fn.TryGetProperty("arguments", out var argsEl)
                    ? argsEl.ValueKind == JsonValueKind.String ? argsEl.GetString() ?? "{}" : argsEl.GetRawText()
                    : "{}";
                result.Add(new ToolCall { Id = $"ollama-{idx++}", Name = name, InputJson = argsStr });
            }
        }
        catch (JsonException) { /* ignore parse errors */ }
        return result;
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the Ollama model name, falling back to "llama3.2" when not configured.
    /// </summary>
    private string ResolveModel()
        => string.IsNullOrWhiteSpace(_settings.OllamaModel) ? "llama3.2" : _settings.OllamaModel;

    /// <summary>
    /// Maps <see cref="AssistantMessage"/> list to <see cref="OllamaMessage"/> list.
    /// </summary>
    private static List<OllamaMessage> MapToOllamaMessages(IReadOnlyList<AssistantMessage> messages)
    {
        var result = new List<OllamaMessage>(messages.Count);
        foreach (var msg in messages)
        {
            var role = msg.Role switch
            {
                AssistantRole.System    => "system",
                AssistantRole.User      => "user",
                AssistantRole.Assistant => "assistant",
                _ => "user"
            };
            result.Add(new OllamaMessage { Role = role, Content = msg.Content });
        }
        return result;
    }

    /// <summary>
    /// Maps messages to raw objects that can carry tool-related fields
    /// (tool results and assistant turns with tool_calls).
    /// </summary>
    private static List<object> MapToOllamaMessagesWithTools(IReadOnlyList<AssistantMessage> messages)
    {
        var result = new List<object>(messages.Count);
        foreach (var msg in messages)
        {
            if (msg.Role == AssistantRole.Tool)
            {
                // Ollama tool result: role="tool", content=result
                result.Add(new { role = "tool", content = msg.Content });
            }
            else if (msg.Role == AssistantRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                result.Add(new
                {
                    role       = "assistant",
                    content    = msg.Content ?? string.Empty,
                    tool_calls = msg.ToolCalls.Select(tc => new
                    {
                        type     = "function",
                        function = new { name = tc.Name, arguments = JsonDocument.Parse(tc.InputJson).RootElement }
                    }).ToArray()
                });
            }
            else
            {
                var role = msg.Role switch
                {
                    AssistantRole.System    => "system",
                    AssistantRole.User      => "user",
                    AssistantRole.Assistant => "assistant",
                    _ => "user"
                };
                result.Add(new { role, content = msg.Content });
            }
        }
        return result;
    }

    /// <summary>
    /// Core streaming implementation shared by both legacy and new APIs.
    /// Sends a streaming request to Ollama and yields content tokens.
    /// </summary>
    private async IAsyncEnumerable<string> StreamOllamaAsync(
        List<OllamaMessage> messages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var modelToUse = ResolveModel();

        var request = new OllamaRequest
        {
            Model = modelToUse,
            Stream = true,
            Messages = messages
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var url = $"{_settings.OllamaBaseUrl.TrimEnd('/')}/api/chat";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            yield return $"Erro {(int)response.StatusCode} do Ollama: {errorBody.Trim()}";
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaResponse>(line, _jsonOptions);
            }
            catch (JsonException)
            {
                // Skip malformed lines
                continue;
            }

            if (chunk is null)
                continue;

            var content = chunk.Content;
            if (!string.IsNullOrEmpty(content))
                yield return content;

            if (chunk.Done)
                yield break;
        }
    }

    private async Task<string> SendSingleMessageAsync(string userPrompt, CancellationToken ct)
    {
        var modelToUse = ResolveModel();

        var request = new OllamaRequest
        {
            Model = modelToUse,
            Stream = false,
            Messages = new List<OllamaMessage>
            {
                new OllamaMessage { Role = "user", Content = userPrompt }
            }
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var url = $"{_settings.OllamaBaseUrl.TrimEnd('/')}/api/chat";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Erro {(int)response.StatusCode} do Ollama: {errorBody.Trim()}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<OllamaResponse>(responseJson, _jsonOptions);

        return result?.Content ?? string.Empty;
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
        CancelCurrentRequest();
        _http.Dispose();
    }
}
