using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// AI assistant provider for OpenRouter (OpenAI-compatible API).
/// Supports any model available on OpenRouter (Claude, GPT, Llama, Mistral, etc.).
/// </summary>
public sealed class OpenRouterProvider : IAssistantProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ISecretStorageService? _secretStorage;
    private readonly AssistantSettings _settings;

    private bool _isInitialized;
    private bool _isDisposed;
    private CancellationTokenSource? _currentCts;

    private const string DefaultModel = "anthropic/claude-sonnet-4";
    private const string BaseUrl = "https://openrouter.ai/api/v1";
    private const string ApiKeySecretName = "ai_openrouter_api_key";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private string? _apiKey;

    public string ProviderName => "OpenRouter";
    public string Name => "openrouter";
    public string DisplayName => "OpenRouter";
    public string DisplayColor => "#89DCEB";  // Catppuccin sky
    public bool IsAvailable => true;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey) || !string.IsNullOrWhiteSpace(_settings.OpenRouterKey);

    public OpenRouterProvider(ISecretStorageService? secretStorage = null, AssistantSettings? settings = null)
    {
        _secretStorage = secretStorage;
        _settings = settings ?? new AssistantSettings();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        if (_secretStorage != null)
        {
            try { _apiKey = await _secretStorage.RetrieveSecretAsync(ApiKeySecretName); }
            catch { _apiKey = null; }
        }

        _isInitialized = true;
    }

    public async Task<AssistantResponse> ChatAsync(IReadOnlyList<AssistantMessage> messages)
    {
        ThrowIfDisposed();
        if (!_isInitialized) await InitializeAsync();

        var apiKey = !string.IsNullOrWhiteSpace(_settings.OpenRouterKey) ? _settings.OpenRouterKey : _apiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return AssistantResponse.Failed("OpenRouter não configurado. Defina uma API key nas Configurações.");

        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        var ct = _currentCts.Token;

        try
        {
            var model = !string.IsNullOrWhiteSpace(_settings.OpenRouterModel) ? _settings.OpenRouterModel : DefaultModel;

            var body = JsonSerializer.Serialize(new
            {
                model,
                messages = messages.Select(m => new { role = m.Role.ToString().ToLowerInvariant(), content = m.Content }),
                temperature = 0.7,
                max_tokens = 4096
            }, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("HTTP-Referer", "https://commanddeck.app");
            request.Headers.Add("X-Title", "CommandDeck");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return AssistantResponse.Failed($"OpenRouter error ({(int)response.StatusCode}): {ExtractError(responseBody)}");

            return ParseResponse(responseBody);
        }
        catch (OperationCanceledException) { return AssistantResponse.Failed("Requisição cancelada."); }
        catch (HttpRequestException ex) { return AssistantResponse.Failed($"Erro de rede: {ex.Message}"); }
        catch (Exception ex) { return AssistantResponse.Failed($"Erro inesperado: {ex.Message}"); }
    }

    public async IAsyncEnumerable<AssistantResponse> StreamChatAsync(
        IReadOnlyList<AssistantMessage> messages,
        Action<string>? onChunk = null)
    {
        ThrowIfDisposed();
        if (!_isInitialized) await InitializeAsync();

        var apiKey = !string.IsNullOrWhiteSpace(_settings.OpenRouterKey) ? _settings.OpenRouterKey : _apiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return AssistantResponse.Failed("OpenRouter nao configurado. Defina uma API key nas Configuracoes.");
            yield break;
        }

        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        var ct = _currentCts.Token;

        var model = !string.IsNullOrWhiteSpace(_settings.OpenRouterModel) ? _settings.OpenRouterModel : DefaultModel;

        var body = JsonSerializer.Serialize(new
        {
            model,
            messages = messages.Select(m => new { role = m.Role.ToString().ToLowerInvariant(), content = m.Content }),
            temperature = 0.7,
            max_tokens = 4096,
            stream = true
        }, JsonOptions);

        HttpResponseMessage? response = null;
        string? networkError = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("HTTP-Referer", "https://commanddeck.app");
            request.Headers.Add("X-Title", "CommandDeck");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                networkError = $"OpenRouter error ({(int)response.StatusCode}): {ExtractError(errorBody)}";
            }
        }
        catch (HttpRequestException ex)
        {
            networkError = $"Erro de rede com OpenRouter: {ex.Message}";
        }

        if (networkError is not null || response is null)
        {
            yield return AssistantResponse.Failed(networkError ?? "Erro desconhecido ao conectar com OpenRouter.");
            yield break;
        }

        using (response)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

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
                            chunk = contentEl.GetString();
                    }
                }
                catch (JsonException) { /* skip malformed lines */ }

                if (!string.IsNullOrEmpty(chunk))
                {
                    onChunk?.Invoke(chunk);
                    yield return AssistantResponse.Success(chunk);
                }
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

    private static AssistantResponse ParseResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var content = string.Empty;

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c))
                    content = c.GetString() ?? string.Empty;
            }

            TokenUsage? usage = null;
            if (root.TryGetProperty("usage", out var usageEl))
            {
                usage = new TokenUsage
                {
                    PromptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                    CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0
                };
            }

            return new AssistantResponse { Content = content, Usage = usage };
        }
        catch (JsonException ex)
        {
            return AssistantResponse.Failed($"Failed to parse response: {ex.Message}");
        }
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? body;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenRouterProvider] Failed to parse error body as JSON: {ex.Message}");
        }
        return body.Length > 200 ? body[..200] + "..." : body;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);
}
