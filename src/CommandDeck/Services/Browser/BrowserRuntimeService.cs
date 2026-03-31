using System.IO;
using CommandDeck.Models.Browser;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CommandDeck.Services.Browser;

public class BrowserRuntimeService : IBrowserRuntimeService, IDisposable
{
    private WebView2? _webView;
    private bool _disposed;

    public BrowserSession Session { get; } = new();
    public bool IsInitialized => _webView?.CoreWebView2 != null;
    public bool CanGoBack => _webView?.CoreWebView2?.CanGoBack ?? false;
    public bool CanGoForward => _webView?.CoreWebView2?.CanGoForward ?? false;
    public Microsoft.Web.WebView2.Core.CoreWebView2? CoreWebView => _webView?.CoreWebView2;

    public event Action<BrowserSessionState>? StateChanged;
    public event Action<string>? TitleChanged;
    public event Action<string>? UrlChanged;
    public event Action<string>? WebMessageReceived;
    public event Action<CoreWebView2ProcessFailedKind>? ProcessFailed;

    public void SetWebView(WebView2 webView)
    {
        _webView = webView;
    }

    public async Task InitializeAsync(nint parentHandle)
    {
        if (_webView == null) return;
        if (_webView.CoreWebView2 != null) return;

        Session.State = BrowserSessionState.Connecting;
        StateChanged?.Invoke(Session.State);

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CommandDeck", "BrowserData");

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await _webView.EnsureCoreWebView2Async(env);

        ConfigureSecurity();
        AttachEvents();

        Session.State = BrowserSessionState.Connected;
        Session.ConnectedAt = DateTime.UtcNow;
        StateChanged?.Invoke(Session.State);
    }

    private void ConfigureSecurity()
    {
        if (_webView?.CoreWebView2 == null) return;

        var settings = _webView.CoreWebView2.Settings;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreDevToolsEnabled = true;
        settings.IsWebMessageEnabled = true;
        settings.IsScriptEnabled = true;
        settings.AreHostObjectsAllowed = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;

        _webView.CoreWebView2.NavigationStarting += (s, e) =>
        {
            var uri = new Uri(e.Uri);
            if (uri.Host != "localhost" && uri.Host != "127.0.0.1" &&
                uri.Scheme != "data" && uri.Scheme != "about")
            {
                e.Cancel = true;
            }
        };

        _webView.CoreWebView2.NewWindowRequested += (s, e) => { e.Handled = true; };
        _webView.CoreWebView2.DownloadStarting += (s, e) => { e.Cancel = true; };
    }

    private void AttachEvents()
    {
        if (_webView?.CoreWebView2 == null) return;

        _webView.CoreWebView2.DocumentTitleChanged += (s, e) =>
        {
            TitleChanged?.Invoke(_webView.CoreWebView2.DocumentTitle ?? "");
        };

        _webView.CoreWebView2.SourceChanged += (s, e) =>
        {
            var url = _webView.CoreWebView2.Source;
            Session.Url = url;
            Session.LastNavigationAt = DateTime.UtcNow;
            if (!Session.NavigationHistory.Contains(url))
                Session.NavigationHistory.Add(url);
            UrlChanged?.Invoke(url);
        };

        _webView.CoreWebView2.NavigationStarting += (s, e) =>
        {
            Session.State = BrowserSessionState.Loading;
            StateChanged?.Invoke(Session.State);
        };

        _webView.CoreWebView2.NavigationCompleted += (s, e) =>
        {
            Session.State = e.IsSuccess
                ? BrowserSessionState.Connected
                : BrowserSessionState.Error;
            StateChanged?.Invoke(Session.State);
        };

        _webView.CoreWebView2.WebMessageReceived += (s, e) =>
        {
            WebMessageReceived?.Invoke(e.WebMessageAsJson);
        };

        _webView.CoreWebView2.ProcessFailed += (s, e) =>
        {
            switch (e.ProcessFailedKind)
            {
                case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                    Session.State = BrowserSessionState.Error;
                    StateChanged?.Invoke(Session.State);
                    ProcessFailed?.Invoke(e.ProcessFailedKind);
                    break;

                case CoreWebView2ProcessFailedKind.RenderProcessExited:
                case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                    Session.State = BrowserSessionState.Error;
                    StateChanged?.Invoke(Session.State);
                    ProcessFailed?.Invoke(e.ProcessFailedKind);
                    break;

                case CoreWebView2ProcessFailedKind.GpuProcessExited:
                    // GPU process exit is non-critical, log only
                    ProcessFailed?.Invoke(e.ProcessFailedKind);
                    break;

                default:
                    Session.State = BrowserSessionState.Error;
                    StateChanged?.Invoke(Session.State);
                    ProcessFailed?.Invoke(e.ProcessFailedKind);
                    break;
            }
        };
    }

    public Task NavigateAsync(string url)
    {
        if (_webView?.CoreWebView2 == null) return Task.CompletedTask;

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "http://" + url;

        _webView.CoreWebView2.Navigate(url);
        Session.Url = url;
        return Task.CompletedTask;
    }

    public Task GoBackAsync()
    {
        if (CanGoBack) _webView!.CoreWebView2.GoBack();
        return Task.CompletedTask;
    }

    public Task GoForwardAsync()
    {
        if (CanGoForward) _webView!.CoreWebView2.GoForward();
        return Task.CompletedTask;
    }

    public Task ReloadAsync()
    {
        _webView?.CoreWebView2?.Reload();
        return Task.CompletedTask;
    }

    public async Task<string> ExecuteScriptAsync(string script)
    {
        if (_webView?.CoreWebView2 == null) return string.Empty;
        return await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    public async Task<byte[]> CaptureScreenshotAsync()
    {
        if (_webView?.CoreWebView2 == null) return Array.Empty<byte>();

        using var stream = new MemoryStream();
        await _webView.CoreWebView2.CapturePreviewAsync(
            CoreWebView2CapturePreviewImageFormat.Png, stream);
        return stream.ToArray();
    }

    public async Task DisposeSafelyAsync()
    {
        if (_disposed) return;

        try
        {
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Navigate("about:blank");
                await Task.Delay(100);
            }
        }
        catch
        {
            // Ignore navigation errors during disposal
        }

        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _webView?.Dispose();
        _webView = null;
    }
}
