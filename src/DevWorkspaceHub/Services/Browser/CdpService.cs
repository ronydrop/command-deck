using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace DevWorkspaceHub.Services.Browser;

public class CdpService : ICdpService
{
    private CoreWebView2? _coreWebView;
    private readonly List<string> _consoleErrors = [];
    private readonly object _errorsLock = new();
    private IDisposable? _consoleSubscription;

    public bool IsAvailable => _coreWebView != null;

    public async Task InitializeAsync(IBrowserRuntimeService browserRuntime)
    {
        if (browserRuntime is not BrowserRuntimeService runtimeService)
            throw new InvalidOperationException("CdpService requires BrowserRuntimeService instance.");

        _coreWebView = runtimeService.CoreWebView
            ?? throw new InvalidOperationException("CoreWebView2 is not initialized.");

        await EnableDomainAsync("Runtime");
        await EnableDomainAsync("Page");

        _consoleSubscription = SubscribeEvent("Runtime.consoleAPICalled", HandleConsoleApiCalled);
    }

    public async Task<JsonDocument> CallMethodAsync(string method, string paramsJson = "{}")
    {
        if (_coreWebView == null)
            throw new InvalidOperationException("CDP is not initialized.");

        var result = await _coreWebView.CallDevToolsProtocolMethodAsync(method, paramsJson);
        return JsonDocument.Parse(result);
    }

    public async Task EnableDomainAsync(string domain)
    {
        await CallMethodAsync($"{domain}.enable", "{}");
    }

    public IDisposable SubscribeEvent(string eventName, Action<JsonDocument> handler)
    {
        if (_coreWebView == null)
            throw new InvalidOperationException("CDP is not initialized.");

        var receiver = _coreWebView.GetDevToolsProtocolEventReceiver(eventName);

        void OnEventReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            handler(doc);
        }

        receiver.DevToolsProtocolEventReceived += OnEventReceived;

        return new CdpEventSubscription(receiver, OnEventReceived);
    }

    public async Task<byte[]> CaptureScreenshotAsync(
        double? clipX = null, double? clipY = null,
        double? clipWidth = null, double? clipHeight = null)
    {
        string paramsJson;

        if (clipX.HasValue && clipY.HasValue && clipWidth.HasValue && clipHeight.HasValue)
        {
            paramsJson = JsonSerializer.Serialize(new
            {
                format = "png",
                clip = new
                {
                    x = clipX.Value,
                    y = clipY.Value,
                    width = clipWidth.Value,
                    height = clipHeight.Value,
                    scale = 1.0
                }
            });
        }
        else
        {
            paramsJson = JsonSerializer.Serialize(new { format = "png" });
        }

        using var result = await CallMethodAsync("Page.captureScreenshot", paramsJson);
        var base64Data = result.RootElement.GetProperty("data").GetString()
            ?? throw new InvalidOperationException("Screenshot response missing data field.");

        return Convert.FromBase64String(base64Data);
    }

    public Task<List<string>> GetConsoleErrorsAsync(int limit = 20)
    {
        lock (_errorsLock)
        {
            var errors = _consoleErrors
                .TakeLast(limit)
                .ToList();

            return Task.FromResult(errors);
        }
    }

    private void HandleConsoleApiCalled(JsonDocument doc)
    {
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            return;

        var type = typeProp.GetString();
        if (type is not ("error" or "warning"))
            return;

        var message = $"[{type?.ToUpperInvariant()}] ";

        if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var arg in args.EnumerateArray())
            {
                if (arg.TryGetProperty("value", out var val))
                    parts.Add(val.ToString());
                else if (arg.TryGetProperty("description", out var desc))
                    parts.Add(desc.GetString() ?? "");
                else if (arg.TryGetProperty("type", out var argType))
                    parts.Add($"[{argType.GetString()}]");
            }
            message += string.Join(" ", parts);
        }

        lock (_errorsLock)
        {
            _consoleErrors.Add(message);

            if (_consoleErrors.Count > 500)
                _consoleErrors.RemoveRange(0, _consoleErrors.Count - 500);
        }
    }

    private sealed class CdpEventSubscription(
        CoreWebView2DevToolsProtocolEventReceiver receiver,
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> handler) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            receiver.DevToolsProtocolEventReceived -= handler;
        }
    }
}
