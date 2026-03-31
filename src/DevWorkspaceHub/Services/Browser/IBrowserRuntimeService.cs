using DevWorkspaceHub.Models.Browser;

namespace DevWorkspaceHub.Services.Browser;

public interface IBrowserRuntimeService
{
    BrowserSession Session { get; }
    bool IsInitialized { get; }

    event Action<BrowserSessionState>? StateChanged;
    event Action<string>? TitleChanged;
    event Action<string>? UrlChanged;

    Task InitializeAsync(nint parentHandle);
    Task NavigateAsync(string url);
    Task GoBackAsync();
    Task GoForwardAsync();
    Task ReloadAsync();
    Task<string> ExecuteScriptAsync(string script);
    Task<byte[]> CaptureScreenshotAsync();
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    void Dispose();
}
