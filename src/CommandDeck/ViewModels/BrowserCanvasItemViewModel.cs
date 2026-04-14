using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// Canvas item ViewModel for a standalone Browser tile.
/// Hosts a WebView2 instance with navigation, desktop/mobile mode toggle,
/// and per-tile independent session. Multiple instances are supported.
/// </summary>
public partial class BrowserCanvasItemViewModel : CanvasItemViewModel
{
    private readonly IEventBusService _eventBus;
    private readonly ITileContextService _tileContext;

    public override CanvasItemType ItemType => CanvasItemType.BrowserWidget;

    /// <summary>Browser card stays at its original size when the canvas is zoomed.</summary>
    public override bool IsZoomImmune => true;

    // ─── Observable state ────────────────────────────────────────────────────

    [ObservableProperty] private string _url = "https://www.google.com";
    [ObservableProperty] private string _addressBarText = "https://www.google.com";
    [ObservableProperty] private string _pageTitle = "Browser";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private bool _isDesktopMode = true;
    [ObservableProperty] private string _statusText = "Pronto";
    [ObservableProperty] private bool _isBrowserReady;

    // User-agent strings
    private const string DesktopUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";
    private const string MobileUserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Mobile Safari/537.36";

    public string CurrentUserAgent => IsDesktopMode ? DesktopUserAgent : MobileUserAgent;

    /// <summary>Raised when the ViewModel wants the control to navigate to a URL.</summary>
    public event Action<string>? NavigateRequested;

    /// <summary>Raised when user-agent changes so the control can update WebView2.</summary>
    public event Action<string>? UserAgentChanged;

    public BrowserCanvasItemViewModel(
        CanvasItemModel model,
        IEventBusService eventBus,
        ITileContextService tileContext)
        : base(model)
    {
        _eventBus = eventBus;
        _tileContext = tileContext;

        // Restore from metadata
        if (model.Metadata.TryGetValue("url", out var savedUrl) && !string.IsNullOrEmpty(savedUrl))
        {
            _url = savedUrl;
            _addressBarText = savedUrl;
        }
        else
        {
            _addressBarText = "https://www.google.com";
        }
        if (model.Metadata.TryGetValue("desktopMode", out var dm))
            _isDesktopMode = dm != "false";
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Navigate()
    {
        var rawUrl = AddressBarText.Trim();
        if (string.IsNullOrEmpty(rawUrl)) return;

        var navigateUrl = NormalizeUrl(rawUrl);
        Url = navigateUrl;
        NavigateRequested?.Invoke(navigateUrl);
        PersistMetadata();

        _tileContext.Set(TileContextKeys.BrowserUrl, navigateUrl, sourceTileId: Id, sourceLabel: PageTitle);
        _eventBus.Publish(BusEventType.Browser_Navigated,
            new { Url = navigateUrl, Source = Id }, source: Id);
    }

    [RelayCommand]
    private void GoBack() { /* Handled by control */ }

    [RelayCommand]
    private void GoForward() { /* Handled by control */ }

    [RelayCommand]
    private void Reload() { /* Handled by control */ }

    [RelayCommand]
    private void ToggleDesktopMode()
    {
        IsDesktopMode = !IsDesktopMode;
        StatusText = IsDesktopMode ? "Modo Desktop" : "Modo Mobile";
        UserAgentChanged?.Invoke(CurrentUserAgent);
        PersistMetadata();
    }

    [RelayCommand]
    private void NavigateHome()
    {
        AddressBarText = "https://google.com";
        Navigate();
    }

    // ─── Callbacks from control ───────────────────────────────────────────────

    /// <summary>Called by the code-behind when WebView2 navigation starts.</summary>
    public void OnNavigationStarted(string url)
    {
        IsLoading = true;
        AddressBarText = url;
        StatusText = "Carregando...";
    }

    /// <summary>Called by the code-behind when WebView2 navigation completes.</summary>
    public void OnNavigationCompleted(string url, string title, bool canBack, bool canForward)
    {
        IsLoading = false;
        Url = url;
        AddressBarText = url;
        PageTitle = string.IsNullOrEmpty(title) ? GetHostname(url) : title;
        CanGoBack = canBack;
        CanGoForward = canForward;
        StatusText = "Pronto";

        Model.Metadata["url"] = url;
        _tileContext.Set(TileContextKeys.BrowserUrl, url, sourceTileId: Id, sourceLabel: PageTitle);
        _tileContext.Set(TileContextKeys.BrowserTitle, PageTitle, sourceTileId: Id);
    }

    private void PersistMetadata()
    {
        Model.Metadata["url"] = Url;
        Model.Metadata["desktopMode"] = IsDesktopMode.ToString().ToLower();
    }

    private static string NormalizeUrl(string raw)
    {
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            return raw;

        // Looks like a search query
        if (!raw.Contains('.') || raw.Contains(' '))
            return $"https://www.google.com/search?q={Uri.EscapeDataString(raw)}";

        return $"https://{raw}";
    }

    private static string GetHostname(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url; }
    }
}
