using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

// ─── Tree node ───────────────────────────────────────────────────────────────

/// <summary>A single node in the file explorer tree (file or folder).</summary>
public partial class FileTreeNode : ObservableObject
{
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isLoading;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public bool IsRoot { get; }
    public ObservableCollection<FileTreeNode> Children { get; } = new();

    public string Icon => IsDirectory
        ? (IsExpanded ? "📂" : "📁")
        : GetFileIcon(Name);

    public FileTreeNode(string fullPath, bool isRoot = false)
    {
        FullPath = fullPath;
        Name = Path.GetFileName(fullPath).Length > 0 ? Path.GetFileName(fullPath) : fullPath;
        IsDirectory = Directory.Exists(fullPath);
        IsRoot = isRoot;

        // Add a dummy child so the TreeView shows the expand arrow
        if (IsDirectory)
            Children.Add(new FileTreeNode("__loading__") { IsLoading = true });
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(Icon));
    }

    private static string GetFileIcon(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".cs"       => "🔵",
        ".ts" or ".tsx" => "🟦",
        ".js" or ".jsx" => "🟨",
        ".py"       => "🐍",
        ".json"     => "📋",
        ".md"       => "📝",
        ".xml" or ".xaml" => "📄",
        ".html" or ".htm" => "🌐",
        ".css" or ".scss" => "🎨",
        ".sql"      => "🗄",
        ".yaml" or ".yml" => "⚙",
        ".sh" or ".bash" or ".ps1" => "⚡",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" => "🖼",
        ".zip" or ".tar" or ".gz" => "📦",
        ".exe" or ".dll" => "⚙",
        ".sln" or ".csproj" => "🔷",
        _           => "📄"
    };
}

// ─── ViewModel ───────────────────────────────────────────────────────────────

/// <summary>
/// Canvas item ViewModel for the File Explorer widget.
/// Displays a directory tree with file open/navigate actions.
/// </summary>
public partial class FileExplorerCanvasItemViewModel : CanvasItemViewModel
{
    private readonly INotificationService _notifications;
    private readonly IEventBusService _eventBus;
    private readonly ITileContextService _tileContext;

    public override CanvasItemType ItemType => CanvasItemType.FileExplorerWidget;

    [ObservableProperty] private string _rootPath = string.Empty;
    [ObservableProperty] private string _title = "Explorador";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Nenhuma pasta aberta";
    [ObservableProperty] private FileTreeNode? _selectedNode;
    [ObservableProperty] private bool _showHiddenFiles;

    public ObservableCollection<FileTreeNode> RootNodes { get; } = new();

    // Filtered search results
    public ObservableCollection<FileTreeNode> SearchResults { get; } = new();
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    private static readonly string[] IgnoredFolders =
        ["bin", "obj", ".git", "node_modules", ".vs", ".idea", "__pycache__", ".cache", "dist", ".next"];

    public FileExplorerCanvasItemViewModel(
        CanvasItemModel model,
        INotificationService notifications,
        IEventBusService eventBus,
        ITileContextService tileContext)
        : base(model)
    {
        _notifications = notifications;
        _eventBus = eventBus;
        _tileContext = tileContext;

        // Restore from metadata
        if (model.Metadata.TryGetValue("rootPath", out var rp) && !string.IsNullOrEmpty(rp)
            && Directory.Exists(rp))
        {
            _ = OpenDirectoryAsync(rp);
        }

        // Auto-open project path if available
        _tileContext.Subscribe(TileContextKeys.ProjectPath, args =>
        {
            if (args.Entry?.Value is string path && !string.IsNullOrEmpty(path)
                && string.IsNullOrEmpty(RootPath))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => _ = OpenDirectoryAsync(path));
            }
        });
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Selecionar pasta para explorar",
            Multiselect = false,
        };

        if (!string.IsNullOrEmpty(RootPath))
            dialog.InitialDirectory = RootPath;

        if (dialog.ShowDialog() != true) return;
        await OpenDirectoryAsync(dialog.FolderName);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(RootPath)) return;
        await OpenDirectoryAsync(RootPath);
    }

    [RelayCommand]
    private void OpenFile(FileTreeNode? node)
    {
        if (node is null || node.IsDirectory) return;
        _eventBus.Publish(BusEventType.Custom,
            new { FilePath = node.FullPath },
            source: Id, channel: "fileexplorer:openfile");
        _tileContext.Set("fileexplorer.selected_file", node.FullPath, sourceTileId: Id, sourceLabel: Title);
        StatusText = node.Name;
    }

    [RelayCommand]
    private async Task ExpandNodeAsync(FileTreeNode? node)
    {
        if (node is null || !node.IsDirectory) return;

        // Already loaded (more than the placeholder)
        if (node.Children.Count > 0 && !node.Children[0].IsLoading)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        node.IsLoading = true;
        node.IsExpanded = true;
        node.Children.Clear();

        await Task.Run(() =>
        {
            try
            {
                var entries = LoadChildren(node.FullPath);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var child in entries)
                        node.Children.Add(child);
                    node.IsLoading = false;
                });
            }
            catch
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    node.IsLoading = false;
                });
            }
        });
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            return;
        }
        _ = SearchFilesAsync(value);
    }

    partial void OnShowHiddenFilesChanged(bool value)
    {
        if (!string.IsNullOrEmpty(RootPath))
            _ = OpenDirectoryAsync(RootPath);
    }

    // ─── Core logic ───────────────────────────────────────────────────────────

    private async Task OpenDirectoryAsync(string path)
    {
        IsLoading = true;
        StatusText = "Carregando...";
        RootNodes.Clear();

        try
        {
            var root = new FileTreeNode(path, isRoot: true) { IsExpanded = true };
            root.Children.Clear();

            await Task.Run(() =>
            {
                var children = LoadChildren(path);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var c in children)
                        root.Children.Add(c);
                });
            });

            RootPath = path;
            Title = Path.GetFileName(path).Length > 0 ? Path.GetFileName(path) : path;
            Model.Metadata["rootPath"] = path;

            RootNodes.Add(root);
            StatusText = $"{root.Children.Count} itens";

            _tileContext.Set("fileexplorer.root_path", path, sourceTileId: Id, sourceLabel: Title);
            _eventBus.Publish(BusEventType.Project_Opened, path, source: Id);
        }
        catch (Exception ex)
        {
            StatusText = $"Erro: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private FileTreeNode[] LoadChildren(string dirPath)
    {
        var result = new System.Collections.Generic.List<FileTreeNode>();
        try
        {
            // Directories first
            foreach (var dir in Directory.EnumerateDirectories(dirPath).OrderBy(d => d))
            {
                var name = Path.GetFileName(dir);
                if (!ShowHiddenFiles && (name.StartsWith('.') || IgnoredFolders.Contains(name, StringComparer.OrdinalIgnoreCase)))
                    continue;
                result.Add(new FileTreeNode(dir));
            }

            // Then files
            foreach (var file in Directory.EnumerateFiles(dirPath).OrderBy(f => f))
            {
                var name = Path.GetFileName(file);
                if (!ShowHiddenFiles && name.StartsWith('.'))
                    continue;
                result.Add(new FileTreeNode(file));
            }
        }
        catch { /* ignore access denied */ }
        return result.ToArray();
    }

    private async Task SearchFilesAsync(string query)
    {
        if (string.IsNullOrEmpty(RootPath)) return;
        SearchResults.Clear();

        var results = new System.Collections.Generic.List<FileTreeNode>();
        await Task.Run(() =>
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
                {
                    if (results.Count >= 100) break;
                    var name = Path.GetFileName(file);
                    if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        results.Add(new FileTreeNode(file));
                }
            }
            catch { /* ignore */ }
        });

        foreach (var r in results)
            SearchResults.Add(r);

        StatusText = $"{results.Count} resultados para \"{query}\"";
    }
}
