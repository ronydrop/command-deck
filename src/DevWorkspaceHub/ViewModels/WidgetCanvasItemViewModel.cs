using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// Canvas item that represents a non-terminal widget (Git info, Process monitor, Shortcuts).
/// The actual widget UI is rendered by the appropriate control via DataTemplateSelector.
/// </summary>
public partial class WidgetCanvasItemViewModel : CanvasItemViewModel
{
    private readonly IGitService? _gitService;
    private readonly IProcessMonitorService? _processMonitorService;
    private readonly IWorkspaceService? _workspaceService;

    [ObservableProperty] private WidgetType _widgetType;

    // ─── Git widget data ─────────────────────────────────────────────────────

    [ObservableProperty] private GitInfo? _gitInfo;

    /// <summary>Absolute path of the repository to display (set externally).</summary>
    [ObservableProperty] private string _repositoryPath = string.Empty;

    // ─── Process widget data ─────────────────────────────────────────────────

    public ObservableCollection<ProcessInfo> Processes { get; } = new();

    // ─── Shortcut widget data ────────────────────────────────────────────────

    public ObservableCollection<string> Shortcuts { get; } = new();

    // ─── Note widget data ────────────────────────────────────────────────────

    [ObservableProperty] private string _noteText = string.Empty;

    [ObservableProperty] private string _noteColor = "#f9e2af"; // Catppuccin Yellow

    // ─── Image widget data ───────────────────────────────────────────────────

    /// <summary>Absolute path to the image file on disk.</summary>
    [ObservableProperty] private string _imagePath = string.Empty;

    /// <summary>Loaded image source for display.</summary>
    [ObservableProperty] private ImageSource? _imageSource;

    /// <summary>Image opacity (0.0–1.0), default fully opaque.</summary>
    [ObservableProperty] private double _imageOpacity = 1.0;

    public override CanvasItemType ItemType => WidgetType switch
    {
        WidgetType.Git => CanvasItemType.GitWidget,
        WidgetType.Process => CanvasItemType.ProcessWidget,
        WidgetType.Note => CanvasItemType.NoteWidget,
        WidgetType.Image => CanvasItemType.ImageWidget,
        _ => CanvasItemType.ShortcutWidget
    };

    // ─── Constructor ─────────────────────────────────────────────────────────

    public WidgetCanvasItemViewModel(
        WidgetType type,
        CanvasItemModel model,
        IGitService? gitService = null,
        IProcessMonitorService? processMonitorService = null,
        IWorkspaceService? workspaceService = null)
        : base(model)
    {
        _widgetType = type;
        _gitService = gitService;
        _processMonitorService = processMonitorService;
        _workspaceService = workspaceService;

        if (type == WidgetType.Process && processMonitorService is not null)
        {
            processMonitorService.ProcessesUpdated += OnProcessesUpdated;
        }

        // Restore note data from metadata
        if (type == WidgetType.Note)
        {
            if (model.Metadata.TryGetValue("noteText", out var text))
                _noteText = text;
            if (model.Metadata.TryGetValue("noteColor", out var color))
                _noteColor = color;
        }

        // Restore image data from metadata
        if (type == WidgetType.Image)
        {
            if (model.Metadata.TryGetValue("imagePath", out var imgPath))
            {
                _imagePath = imgPath;
                LoadImageFromPath(imgPath);
            }
            if (model.Metadata.TryGetValue("imageOpacity", out var opStr)
                && double.TryParse(opStr, System.Globalization.CultureInfo.InvariantCulture, out var op))
            {
                _imageOpacity = op;
            }
        }
    }

    // ─── Git refresh ─────────────────────────────────────────────────────────

    public async Task RefreshGitAsync()
    {
        if (_gitService is null || string.IsNullOrEmpty(RepositoryPath)) return;
        GitInfo = await _gitService.GetGitInfoAsync(RepositoryPath);
    }

    partial void OnRepositoryPathChanged(string value)
    {
        _ = RefreshGitAsync();
    }

    // ─── Process data ────────────────────────────────────────────────────────

    private void OnProcessesUpdated(System.Collections.Generic.List<ProcessInfo> processes)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Processes.Clear();
            foreach (var p in processes)
                Processes.Add(p);
        });
    }

    public async Task KillProcessAsync(int pid)
    {
        if (_processMonitorService is not null)
            await _processMonitorService.KillProcessAsync(pid);
    }

    // ─── Note data sync ───────────────────────────────────────────────────────

    partial void OnNoteTextChanged(string value)
    {
        Model.Metadata["noteText"] = value;
    }

    partial void OnNoteColorChanged(string value)
    {
        Model.Metadata["noteColor"] = value;
    }

    // ─── Image data sync ──────────────────────────────────────────────────────

    partial void OnImagePathChanged(string value)
    {
        Model.Metadata["imagePath"] = value;
        LoadImageFromPath(value);
    }

    partial void OnImageOpacityChanged(double value)
    {
        Model.Metadata["imageOpacity"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Loads a BitmapImage from the given file path (frozen for thread safety).</summary>
    private void LoadImageFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ImageSource = null;
            return;
        }
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            ImageSource = bitmap;
        }
        catch
        {
            ImageSource = null;
        }
    }

    /// <summary>
    /// Saves a BitmapSource to the images directory and sets the ImagePath.
    /// Called when the user pastes or drops an image.
    /// </summary>
    public void SetImageFromBitmap(BitmapSource bitmap)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DevWorkspaceHub", "images");
        Directory.CreateDirectory(dir);

        var fileName = $"img_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        var filePath = Path.Combine(dir, fileName);

        using (var fs = new FileStream(filePath, FileMode.Create))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(fs);
        }

        ImagePath = filePath;
    }

    // ─── Shortcut execution ──────────────────────────────────────────────────

    public async Task ExecuteShortcutAsync(string command)
    {
        if (_workspaceService?.ActiveTerminal is { } active)
            await active.Terminal.ExecuteCommandAsync(command);
    }
}
