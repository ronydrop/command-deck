using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommandDeck.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace CommandDeck.Controls;

public partial class CodeEditorWidgetControl : UserControl
{
    private CodeEditorCanvasItemViewModel? _vm;
    private bool _editorReady;

    public CodeEditorWidgetControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Keyboard shortcuts
        KeyDown += OnKeyDown;
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulateLanguageCombo();
        _ = InitWebViewAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Monaco.WebMessageReceived -= OnWebMessageReceived;
        Monaco.NavigationCompleted -= OnNavigationCompleted;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = e.NewValue as CodeEditorCanvasItemViewModel;

        if (_editorReady && _vm is not null)
            SyncVmToEditor();

        // Keep language combo in sync
        if (_vm is not null)
            SyncLanguageCombo(_vm.Language);
    }

    // ─── WebView2 init ────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task InitWebViewAsync()
    {
        try
        {
            await Monaco.EnsureCoreWebView2Async();
            Monaco.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Monaco.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // Load the bundled HTML from the assembly resources
            var htmlPath = GetMonacoHtmlPath();
            Monaco.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CodeEditor] WebView2 init failed: {ex.Message}");
            if (_vm is not null)
                _vm.StatusText = "WebView2 não disponível";
        }
    }

    private static string GetMonacoHtmlPath()
    {
        // Try to use the file next to the assembly
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        var path = Path.Combine(assemblyDir, "Resources", "monaco-editor.html");
        if (File.Exists(path)) return path;

        // Fallback: project Resources directory (dev)
        var devPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "monaco-editor.html");
        return devPath;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // Editor becomes ready after the 'ready' message from JS
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson);
            var type = msg.GetProperty("type").GetString();

            switch (type)
            {
                case "ready":
                    _editorReady = true;
                    Dispatcher.Invoke(SyncVmToEditor);
                    break;

                case "contentChanged":
                    var content = msg.GetProperty("content").GetString() ?? string.Empty;
                    Dispatcher.Invoke(() => _vm?.NotifyContentChanged(content));
                    break;

                case "cursorChanged":
                    var line = msg.GetProperty("line").GetInt32();
                    var col  = msg.GetProperty("column").GetInt32();
                    Dispatcher.Invoke(() => _vm?.NotifyCursorChanged(line, col));
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CodeEditor] WebMessage parse error: {ex.Message}");
        }
    }

    // ─── Host → Editor bridge ─────────────────────────────────────────────────

    private void SyncVmToEditor()
    {
        if (!_editorReady || _vm is null || Monaco.CoreWebView2 is null) return;

        var contentJson = JsonSerializer.Serialize(_vm.Content);
        var langJson = JsonSerializer.Serialize(_vm.Language);
        Monaco.CoreWebView2.ExecuteScriptAsync($"window.setContent({contentJson}, {langJson})");
        Monaco.CoreWebView2.ExecuteScriptAsync($"window.setOption('minimap', {{ enabled: {(_vm.ShowMinimap ? "true" : "false")} }})");
        Monaco.CoreWebView2.ExecuteScriptAsync($"window.setOption('wordWrap', '{(_vm.WordWrap ? "on" : "off")}')");
        Monaco.CoreWebView2.ExecuteScriptAsync($"window.setOption('fontSize', {_vm.FontSize})");
    }

    private void SendToEditor(string script)
    {
        if (_editorReady && Monaco.CoreWebView2 is not null)
            Monaco.CoreWebView2.ExecuteScriptAsync(script);
    }

    // ─── Toolbar handlers ─────────────────────────────────────────────────────

    private void OnFormatClick(object sender, RoutedEventArgs e)
        => SendToEditor("window.formatDocument()");

    private void OnMinimapToggle(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        SendToEditor($"window.setOption('minimap', {{ enabled: {(_vm.ShowMinimap ? "true" : "false")} }})");
    }

    private void OnWordWrapToggle(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        SendToEditor($"window.setOption('wordWrap', '{(_vm.WordWrap ? "on" : "off")}')");
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedItem is not string langId || _vm is null) return;
        _vm.SetLanguageCommand.Execute(langId);
        SendToEditor($"window.setLanguage('{langId}')");
    }

    // ─── Language combo ───────────────────────────────────────────────────────

    private void PopulateLanguageCombo()
    {
        LanguageCombo.SelectionChanged -= OnLanguageSelectionChanged;
        LanguageCombo.Items.Clear();

        foreach (var (id, _) in CodeEditorCanvasItemViewModel.SupportedLanguages)
            LanguageCombo.Items.Add(id);

        SyncLanguageCombo(_vm?.Language ?? "plaintext");
        LanguageCombo.SelectionChanged += OnLanguageSelectionChanged;
    }

    private void SyncLanguageCombo(string langId)
    {
        LanguageCombo.SelectionChanged -= OnLanguageSelectionChanged;
        LanguageCombo.SelectedItem = langId;
        LanguageCombo.SelectionChanged += OnLanguageSelectionChanged;
    }

    // ─── Keyboard shortcuts ───────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;

        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _vm.SaveFileCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _vm.OpenFileCommand.Execute(null);
            e.Handled = true;
        }
    }
}
