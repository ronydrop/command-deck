using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommandDeck.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace CommandDeck.Controls;

public partial class BrowserWidgetControl : UserControl
{
    private BrowserCanvasItemViewModel? _vm;

    public BrowserWidgetControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = WebView.EnsureCoreWebView2Async();
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
        }
    }

    private void OnCoreWebView2Initialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _vm is null) return;

        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        // Set User-Agent
        WebView.CoreWebView2.Settings.UserAgent = _vm.CurrentUserAgent;

        _vm.IsBrowserReady = true;

        // Navigate to saved URL
        var startUrl = _vm.Url;
        if (!string.IsNullOrEmpty(startUrl) && startUrl != "about:blank")
            WebView.CoreWebView2.Navigate(startUrl);
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

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2?.CanGoBack == true)
            WebView.CoreWebView2.GoBack();
    }

    private void OnForwardClick(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2?.CanGoForward == true)
            WebView.CoreWebView2.GoForward();
    }

    private void OnReloadClick(object sender, RoutedEventArgs e)
        => WebView.CoreWebView2?.Reload();

    private void OnHomeClick(object sender, RoutedEventArgs e)
        => _vm?.NavigateHomeCommand.Execute(null);

    private void OnDesktopModeClick(object sender, RoutedEventArgs e)
        => _vm?.ToggleDesktopModeCommand.Execute(null);

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
