using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// Canvas item ViewModel for the Code Editor widget.
/// Hosts a Monaco Editor via WebView2 and provides file open/save operations.
/// Publishes <see cref="BusEventType.Custom"/> events on "codeeditor:changed" for cross-tile use.
/// </summary>
public partial class CodeEditorCanvasItemViewModel : CanvasItemViewModel
{
    private readonly INotificationService _notifications;
    private readonly IEventBusService _eventBus;
    private readonly ITileContextService _tileContext;

    public override CanvasItemType ItemType => CanvasItemType.CodeEditorWidget;

    // ─── Observable state ────────────────────────────────────────────────────

    [ObservableProperty] private string _title = "editor.cs";
    [ObservableProperty] private string _currentFilePath = string.Empty;
    [ObservableProperty] private string _language = "csharp";
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Pronto";
    [ObservableProperty] private int _cursorLine = 1;
    [ObservableProperty] private int _cursorColumn = 1;
    [ObservableProperty] private bool _showMinimap = true;
    [ObservableProperty] private bool _wordWrap = false;
    [ObservableProperty] private int _fontSize = 13;

    // ─── Language display names ───────────────────────────────────────────────

    public static readonly (string Id, string Label)[] SupportedLanguages =
    [
        ("csharp",     "C#"),
        ("javascript", "JavaScript"),
        ("typescript", "TypeScript"),
        ("python",     "Python"),
        ("json",       "JSON"),
        ("xml",        "XML"),
        ("yaml",       "YAML"),
        ("markdown",   "Markdown"),
        ("css",        "CSS"),
        ("html",       "HTML"),
        ("sql",        "SQL"),
        ("shell",      "Shell"),
        ("plaintext",  "Plain Text"),
    ];

    public CodeEditorCanvasItemViewModel(
        CanvasItemModel model,
        INotificationService notifications,
        IEventBusService eventBus,
        ITileContextService tileContext)
        : base(model)
    {
        _notifications = notifications;
        _eventBus = eventBus;
        _tileContext = tileContext;

        // Restore from persisted metadata
        if (model.Metadata.TryGetValue("filePath", out var fp) && !string.IsNullOrEmpty(fp))
        {
            _currentFilePath = fp;
            _title = Path.GetFileName(fp);
        }
        if (model.Metadata.TryGetValue("language", out var lang) && !string.IsNullOrEmpty(lang))
            _language = lang;
        if (model.Metadata.TryGetValue("content", out var cnt) && !string.IsNullOrEmpty(cnt))
            _content = cnt;
        if (model.Metadata.TryGetValue("showMinimap", out var mm))
            _showMinimap = mm != "false";
        if (model.Metadata.TryGetValue("wordWrap", out var ww))
            _wordWrap = ww == "true";
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    /// <summary>Opens a file via the standard Windows file dialog.</summary>
    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "All files (*.*)|*.*|" +
                     "C# files (*.cs)|*.cs|" +
                     "JavaScript (*.js;*.ts;*.jsx;*.tsx)|*.js;*.ts;*.jsx;*.tsx|" +
                     "Web files (*.html;*.css)|*.html;*.css|" +
                     "Config files (*.json;*.yaml;*.yml;*.xml)|*.json;*.yaml;*.yml;*.xml|" +
                     "Python (*.py)|*.py|" +
                     "Markdown (*.md)|*.md",
            Title = "Abrir arquivo no editor"
        };

        if (dialog.ShowDialog() != true) return;

        IsLoading = true;
        StatusText = "Carregando...";
        try
        {
            var text = await File.ReadAllTextAsync(dialog.FileName);
            CurrentFilePath = dialog.FileName;
            Title = Path.GetFileName(dialog.FileName);
            Language = DetectLanguage(dialog.FileName);
            Content = text;
            IsDirty = false;
            StatusText = $"Aberto: {Title}";

            PersistMetadata();
            _eventBus.Publish(BusEventType.Custom,
                new { FilePath = CurrentFilePath, Language, Content },
                source: Id, channel: "codeeditor:opened");
        }
        catch (Exception ex)
        {
            StatusText = $"Erro: {ex.Message}";
            _notifications.Notify("Falha ao abrir arquivo", NotificationType.Error,
                NotificationSource.System, message: ex.Message);
        }
        finally { IsLoading = false; }
    }

    /// <summary>Saves the current content back to <see cref="CurrentFilePath"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveFileAsync()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            await SaveFileAsAsync();
            return;
        }

        IsLoading = true;
        StatusText = "Salvando...";
        try
        {
            await File.WriteAllTextAsync(CurrentFilePath, Content);
            IsDirty = false;
            StatusText = $"Salvo — {DateTime.Now:HH:mm:ss}";
            PersistMetadata();
        }
        catch (Exception ex)
        {
            StatusText = $"Erro ao salvar: {ex.Message}";
            _notifications.Notify("Falha ao salvar", NotificationType.Error,
                NotificationSource.System, message: ex.Message);
        }
        finally { IsLoading = false; }
    }

    private bool CanSave() => IsDirty || !string.IsNullOrEmpty(CurrentFilePath);

    /// <summary>Saves to a new path via file dialog.</summary>
    [RelayCommand]
    private async Task SaveFileAsAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Title,
            Filter = "All files (*.*)|*.*",
            Title = "Salvar arquivo como"
        };

        if (dialog.ShowDialog() != true) return;

        CurrentFilePath = dialog.FileName;
        Title = Path.GetFileName(dialog.FileName);
        Model.Metadata["filePath"] = CurrentFilePath;

        await SaveFileAsync();
    }

    /// <summary>Called by the code-behind when Monaco reports content change.</summary>
    public void NotifyContentChanged(string newContent)
    {
        if (Content == newContent) return;
        Content = newContent;
        IsDirty = true;
        Model.Metadata["content"] = newContent;

        _tileContext.Set("codeeditor.content", newContent, sourceTileId: Id, sourceLabel: Title);
        _eventBus.Publish(BusEventType.Custom,
            new { Content = newContent, FilePath = CurrentFilePath },
            source: Id, channel: "codeeditor:changed");
    }

    /// <summary>Called by the code-behind when Monaco reports cursor position change.</summary>
    public void NotifyCursorChanged(int line, int column)
    {
        CursorLine = line;
        CursorColumn = column;
    }

    /// <summary>Sends the current content to the active AI chat tile.</summary>
    [RelayCommand]
    private void SendToAI()
    {
        _tileContext.Set("codeeditor.content", Content, sourceTileId: Id, sourceLabel: Title);
        _eventBus.Publish(BusEventType.Chat_ContextInjected,
            $"Analise este código ({Language}):\n\n```{Language}\n{Content}\n```",
            source: Id);
        StatusText = "Enviado para o chat IA";
    }

    /// <summary>Sets the editor language and persists it.</summary>
    [RelayCommand]
    private void SetLanguage(string lang)
    {
        Language = lang;
        Model.Metadata["language"] = lang;
        _eventBus.Publish(BusEventType.Custom,
            new { Language = lang }, source: Id, channel: "codeeditor:language");
    }

    partial void OnLanguageChanged(string value)
    {
        Model.Metadata["language"] = value;
    }

    partial void OnShowMinimapChanged(bool value)
    {
        Model.Metadata["showMinimap"] = value.ToString().ToLower();
    }

    partial void OnWordWrapChanged(bool value)
    {
        Model.Metadata["wordWrap"] = value.ToString().ToLower();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void PersistMetadata()
    {
        Model.Metadata["filePath"] = CurrentFilePath;
        Model.Metadata["language"] = Language;
        Model.Metadata["content"] = Content;
    }

    private static string DetectLanguage(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs"                   => "csharp",
        ".js" or ".jsx"         => "javascript",
        ".ts" or ".tsx"         => "typescript",
        ".py"                   => "python",
        ".json"                 => "json",
        ".xml" or ".xaml"       => "xml",
        ".yaml" or ".yml"       => "yaml",
        ".md"                   => "markdown",
        ".css" or ".scss"       => "css",
        ".html" or ".htm"       => "html",
        ".sql"                  => "sql",
        ".sh" or ".bash" or ".ps1" => "shell",
        _ => "plaintext"
    };
}
