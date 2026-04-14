using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommandDeck.ViewModels;
using CommandDeck.Views;
using Microsoft.Web.WebView2.Core;

namespace CommandDeck.Controls;

public partial class BrowserWidgetControl : UserControl
{
    private BrowserCanvasItemViewModel? _vm;

    // ── Minimize state ────────────────────────────────────────────────────────
    private bool _isMinimized;
    private double _heightBeforeMinimize;

    // ── Header drag state ─────────────────────────────────────────────────────
    private bool _isDragging;
    private Point _dragStart;
    private double _vmXAtDragStart;
    private double _vmYAtDragStart;

    public BrowserWidgetControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Suppress the CanvasCardControl's generic titlebar — browser uses its own chrome
        if (_vm is not null)
            _vm.HideTitlebar = true;

        try
        {
            var tileId = _vm?.Id.ToString() ?? "default";
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CommandDeck", "WebView2Cache", tileId);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await WebView.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            if (_vm is not null)
            {
                _vm.IsBrowserReady = false;
                _vm.StatusText = $"Erro ao inicializar browser: {ex.Message}";
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.NavigateRequested -= OnVmNavigateRequested;
            _vm.UserAgentChanged -= OnVmUserAgentChanged;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.NavigateRequested -= OnVmNavigateRequested;
            _vm.UserAgentChanged -= OnVmUserAgentChanged;
        }

        _vm = e.NewValue as BrowserCanvasItemViewModel;

        if (_vm is not null)
        {
            _vm.NavigateRequested += OnVmNavigateRequested;
            _vm.UserAgentChanged += OnVmUserAgentChanged;
            // Hide CanvasCardControl's generic titlebar — browser has its own chrome
            _vm.HideTitlebar = true;
        }
    }

    private void OnCoreWebView2Initialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _vm is null) return;

        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        WebView.CoreWebView2.Settings.UserAgent = _vm.CurrentUserAgent;

        _vm.IsBrowserReady = true;

        // Force layout pass after entrance animation so HWND gets correct Win32 position
        Dispatcher.InvokeAsync(() =>
        {
            WebView.InvalidateArrange();
            WebView.UpdateLayout();

            var startUrl = _vm.Url;
            if (!string.IsNullOrEmpty(startUrl) && startUrl != "about:blank")
                WebView.CoreWebView2.Navigate(startUrl);
            else
                WebView.CoreWebView2.Navigate("https://www.google.com");
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _vm?.OnNavigationStarted(e.Uri);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_vm is null || WebView.CoreWebView2 is null) return;
        var url = WebView.Source?.ToString() ?? string.Empty;
        var title = WebView.CoreWebView2.DocumentTitle;
        var canBack = WebView.CoreWebView2.CanGoBack;
        var canForward = WebView.CoreWebView2.CanGoForward;
        _vm.OnNavigationCompleted(url, title, canBack, canForward);
    }

    private void OnVmNavigateRequested(string url)
    {
        if (WebView.CoreWebView2 is not null)
            WebView.CoreWebView2.Navigate(url);
    }

    private void OnVmUserAgentChanged(string userAgent)
    {
        if (WebView.CoreWebView2 is not null)
        {
            WebView.CoreWebView2.Settings.UserAgent = userAgent;
            WebView.CoreWebView2.Reload();
        }
    }

    // ── Traffic lights ────────────────────────────────────────────────────────

    private void OnCloseDotClick(object sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(CanvasCardControl.CardCloseRequestedEvent, this));

    private void OnMinimizeDotClick(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        _isMinimized = !_isMinimized;

        if (_isMinimized)
        {
            _heightBeforeMinimize = _vm.Height;
            _vm.Height = 43;   // 3px AccentStrip + 40px header
            MainGrid.RowDefinitions[1].Height = new GridLength(0);
        }
        else
        {
            MainGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            _vm.Height = _heightBeforeMinimize > 0 ? _heightBeforeMinimize : 500;
        }
    }

    private void OnMaximizeDotClick(object sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(CanvasCardControl.CardFocusRequestedEvent, this));

    // ── Header drag (mirrors CanvasCardControl.OnTitleBarMouse*) ─────────────

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // Let traffic-light and nav buttons handle their own clicks
        if (e.OriginalSource is DependencyObject src && IsDescendantOfButton(src)) return;

        // No drag in tiled mode
        if (IsTiledMode()) return;

        if (_vm is null) return;

        _isDragging = true;
        _dragStart = e.GetPosition(null); // screen coords
        _vmXAtDragStart = _vm.X;
        _vmYAtDragStart = _vm.Y;

        HeaderBorder.CaptureMouse();
        e.Handled = true;
    }

    private void OnHeaderMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _vm is null) return;

        var current = e.GetPosition(null);
        double dx = current.X - _dragStart.X;
        double dy = current.Y - _dragStart.Y;

        double zoom = GetCanvasZoom();
        _vm.X = _vmXAtDragStart + dx / zoom;
        _vm.Y = _vmYAtDragStart + dy / zoom;
    }

    private void OnHeaderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        HeaderBorder.ReleaseMouseCapture();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private double GetCanvasZoom()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is TerminalCanvasView canvasView)
                return canvasView.CurrentZoom;
        }
        return 1.0;
    }

    private static bool IsTiledMode()
    {
        // Walk up: TerminalCanvasView is always in the visual tree above
        var mainVm = Application.Current?.MainWindow?.DataContext as ViewModels.MainViewModel;
        return mainVm?.CanvasViewModel?.IsTiledMode == true;
    }

    private static bool IsDescendantOfButton(DependencyObject obj)
    {
        DependencyObject? current = obj;
        while (current is not null)
        {
            if (current is Button) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2?.CanGoBack == true)
            WebView.CoreWebView2.GoBack();
    }

    private void OnReloadClick(object sender, RoutedEventArgs e)
        => WebView.CoreWebView2?.Reload();

    private void OnAddressBarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm?.NavigateCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            WebView.CoreWebView2?.Reload();
            e.Handled = true;
        }
    }

    private void OnAddressBarFocus(object sender, RoutedEventArgs e)
        => (sender as TextBox)?.SelectAll();
}
