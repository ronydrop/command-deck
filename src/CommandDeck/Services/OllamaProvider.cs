using System;
using System.Collections.Generic;
using System.IO;
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
public sealed class OllamaProvider : IAssistantProvider
{
    private readonly HttpClient _http;
    private readonly AssistantSettings _settings;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OllamaProvider(HttpClient http, AssistantSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public string ProviderName => "Ollama";

    /// <summary>
    /// Checks availability by doing a quick GET /api/tags with a 2-second timeout.
    /// Never throws — returns false on any error.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var url = $"{_settings.OllamaBaseUrl.TrimEnd('/')}/api/tags";
                var response = _http.GetAsync(url, cts.Token).GetAwaiter().GetResult();
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
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

    /// <summary>
    /// Streams a chat response token-by-token using Ollama's JSON-line streaming format.
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        IEnumerable<(string role, string content)> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new List<OllamaMessage>();
        foreach (var (role, content) in history)
            messages.Add(new OllamaMessage { Role = role, Content = content });

        var modelToUse = string.IsNullOrWhiteSpace(_settings.OllamaModel) ? "llama3.2" : _settings.OllamaModel;

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

        response.EnsureSuccessStatusCode();

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

    // ─── Private helpers ─────────────────────────────────────────────────────

    private async Task<string> SendSingleMessageAsync(string userPrompt, CancellationToken ct)
    {
        var modelToUse = string.IsNullOrWhiteSpace(_settings.OllamaModel) ? "llama3.2" : _settings.OllamaModel;

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
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<OllamaResponse>(responseJson, _jsonOptions);

        return result?.Content ?? string.Empty;
    }
}
