using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// Canvas item that represents a non-terminal widget (Git info, Process monitor, Shortcuts).
/// The actual widget UI is rendered by the appropriate control via DataTemplateSelector.
/// </summary>
public partial class WidgetCanvasItemViewModel : CanvasItemViewModel
{
    private readonly IGitService? _gitService;
    private readonly IProcessMonitorService? _processMonitorService;
    private readonly IWorkspaceService? _workspaceService;
    private readonly INotificationService? _notificationService;
    private readonly IKanbanService? _kanbanService;
    private readonly IAssistantService? _assistantService;
    private readonly ITaskAutomationService? _taskAutomationService;
    private readonly IClaudeUsageService? _claudeUsageService;

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

    // ─── Kanban widget data ──────────────────────────────────────────────────
    [ObservableProperty] private KanbanBoard? _kanbanBoard;

    /// <summary>
    /// Per-column view-models driving the dynamic column layout.
    /// Each <see cref="KanbanColumnViewModel"/> owns its cards collection.
    /// </summary>
    public ObservableCollection<KanbanColumnViewModel> KanbanColumnViewModels { get; } = new();

    /// <summary>Total card count across all columns for the header badge.</summary>
    public int KanbanCardCount =>
        KanbanColumnViewModels.Sum(c => c.Cards.Count);

    // ─── Chat widget data ────────────────────────────────────────────────────
    [ObservableProperty] private string _chatInputText = string.Empty;
    [ObservableProperty] private bool _chatIsLoading;
    public ObservableCollection<ChatMessage> ChatMessages { get; } = new();
    private CancellationTokenSource? _chatCts;

    // ─── System Monitor widget data ──────────────────────────────────────────
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double _memoryUsedGb;
    [ObservableProperty] private double _memoryTotalGb;
    [ObservableProperty] private string _systemHostname = Environment.MachineName;
    private System.Windows.Threading.DispatcherTimer? _sysMonTimer;
    private System.Diagnostics.PerformanceCounter? _cpuCounter;

    public override CanvasItemType ItemType => WidgetType switch
    {
        WidgetType.Git           => CanvasItemType.GitWidget,
        WidgetType.Process       => CanvasItemType.ProcessWidget,
        WidgetType.Note          => CanvasItemType.NoteWidget,
        WidgetType.Image         => CanvasItemType.ImageWidget,
        WidgetType.Kanban        => CanvasItemType.KanbanWidget,
        WidgetType.Chat          => CanvasItemType.ChatWidget,
        WidgetType.SystemMonitor => CanvasItemType.SystemMonitorWidget,
        WidgetType.TokenCounter  => CanvasItemType.TokenCounterWidget,
        WidgetType.Pomodoro      => CanvasItemType.PomodoroWidget,
        _                        => CanvasItemType.ShortcutWidget
    };

    // ─── Constructor ─────────────────────────────────────────────────────────

    public WidgetCanvasItemViewModel(
        WidgetType type,
        CanvasItemModel model,
        IGitService? gitService = null,
        IProcessMonitorService? processMonitorService = null,
        IWorkspaceService? workspaceService = null,
        INotificationService? notificationService = null,
        IKanbanService? kanbanService = null,
        IAssistantService? assistantService = null,
        ITaskAutomationService? taskAutomationService = null,
        IClaudeUsageService? claudeUsageService = null)
        : base(model)
    {
        _widgetType = type;
        _gitService = gitService;
        _processMonitorService = processMonitorService;
        _workspaceService = workspaceService;
        _notificationService = notificationService;
        _kanbanService = kanbanService;
        _assistantService = assistantService;
        _taskAutomationService = taskAutomationService;
        _claudeUsageService = claudeUsageService;

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

        // Restore shortcuts from metadata and wire persistence
        if (type == WidgetType.Shortcut)
        {
            if (model.Metadata.TryGetValue("shortcuts", out var json))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                        foreach (var s in list) Shortcuts.Add(s);
                }
                catch
                {
                    // Malformed JSON — start fresh
                }
            }

            Shortcuts.CollectionChanged += OnShortcutsCollectionChanged;
        }

        if (type == WidgetType.Kanban && kanbanService is not null)
        {
            kanbanService.CardUpdated += OnKanbanCardUpdated;
            kanbanService.CardDeleted += OnKanbanCardDeleted;
            _ = LoadKanbanBoardAsync();
        }

        if (type == WidgetType.SystemMonitor)
        {
            StartSystemMonitor();
        }

        if (type == WidgetType.TokenCounter)
        {
            InitTokenCounter();
        }

        if (type == WidgetType.Pomodoro)
        {
            InitPomodoro();
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
            "CommandDeck", "images");
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

    // ─── Shortcut data ────────────────────────────────────────────────────────

    private void OnShortcutsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Model.Metadata["shortcuts"] = JsonSerializer.Serialize(Shortcuts.ToList());
    }

    /// <summary>Adds a command to the shortcut list. No-op if empty or already present.</summary>
    public void AddShortcut(string command)
    {
        var trimmed = command.Trim();
        if (string.IsNullOrEmpty(trimmed) || Shortcuts.Contains(trimmed)) return;
        Shortcuts.Add(trimmed);
    }

    /// <summary>Removes a command from the shortcut list.</summary>
    public void RemoveShortcut(string command) => Shortcuts.Remove(command);

    // ─── Shortcut execution ──────────────────────────────────────────────────

    /// <summary>
    /// Sends the command to the currently active terminal session.
    /// Shows a warning notification if no terminal is active.
    /// </summary>
    public async Task ExecuteShortcutAsync(string command)
    {
        if (_workspaceService?.ActiveTerminal is { } active)
        {
            await active.Terminal.ExecuteCommandAsync(command);
            return;
        }

        _notificationService?.Notify(
            "Nenhum terminal ativo",
            NotificationType.Warning,
            NotificationSource.Terminal,
            "Abra um terminal antes de executar um atalho.");
    }

    // ─── Kanban board operations ──────────────────────────────────────────────

    public async Task LoadKanbanBoardAsync()
    {
        if (_kanbanService is null) return;

        // Use the real workspace ID; fall back to "default" only if nothing is active yet
        var workspaceId = Model.Metadata.TryGetValue("workspaceId", out var wid) && !string.IsNullOrEmpty(wid)
            ? wid
            : _workspaceService?.CurrentWorkspace?.Id ?? "default";

        // Always reload board from DB so columns/cards are fresh on refresh
        var board = await _kanbanService.GetBoardForWorkspaceAsync(workspaceId).ConfigureAwait(false)
                    ?? await _kanbanService.CreateBoardAsync(workspaceId).ConfigureAwait(false);

        Model.Metadata["boardId"]     = board.Id;
        Model.Metadata["workspaceId"] = workspaceId;

        var cards = await _kanbanService.GetCardsForBoardAsync(board.Id).ConfigureAwait(false);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            KanbanBoard = board;
            RebuildColumnViewModels(board.Columns, cards);
        });
    }

    /// <summary>
    /// Rebuilds <see cref="KanbanColumnViewModels"/> from the given columns and cards.
    /// Must be called on the UI thread.
    /// </summary>
    private void RebuildColumnViewModels(IEnumerable<KanbanColumn> columns, IEnumerable<KanbanCard> cards)
    {
        KanbanColumnViewModels.Clear();
        var colVms = columns
            .OrderBy(c => c.SortOrder)
            .Select(c => new KanbanColumnViewModel(c))
            .ToList();

        var cardsByColumn = cards
            .OrderBy(c => c.SortOrder)
            .GroupBy(c => c.ColumnId);

        foreach (var group in cardsByColumn)
        {
            var colVm = colVms.FirstOrDefault(c => c.Id == group.Key);
            if (colVm is null) continue;
            foreach (var card in group)
                colVm.Cards.Add(new KanbanCardViewModel(card));
        }

        foreach (var colVm in colVms)
        {
            colVm.Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(KanbanCardCount));
            KanbanColumnViewModels.Add(colVm);
        }

        OnPropertyChanged(nameof(KanbanCardCount));
    }

    /// <summary>Refreshes only the card collections inside the existing column VMs.</summary>
    public async Task RefreshKanbanCardsAsync()
    {
        if (_kanbanService is null || KanbanBoard is null) return;
        var cards = await _kanbanService.GetCardsForBoardAsync(KanbanBoard.Id).ConfigureAwait(false);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (KanbanBoard is null) return;
            var columns = KanbanColumnViewModels.Select(c => c.Column).ToList();
            RebuildColumnViewModels(columns, cards);
        });
    }

    /// <summary>
    /// Moves a card to a target column and triggers the JustMoved flash animation (1.5 s).
    /// </summary>
    public async Task MoveCardToColumnAsync(string cardId, string targetColumnId)
    {
        if (_kanbanService is null || KanbanBoard is null) return;
        await _kanbanService.MoveCardAsync(cardId, targetColumnId).ConfigureAwait(false);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            // Find the card VM wherever it is and move it to the correct column
            KanbanCardViewModel? cardVm = null;
            KanbanColumnViewModel? sourceColVm = null;
            foreach (var col in KanbanColumnViewModels)
            {
                cardVm = col.Cards.FirstOrDefault(c => c.Id == cardId);
                if (cardVm is not null) { sourceColVm = col; break; }
            }
            var targetColVm = KanbanColumnViewModels.FirstOrDefault(c => c.Id == targetColumnId);
            if (cardVm is null || targetColVm is null || sourceColVm?.Id == targetColumnId) return;

            sourceColVm?.Cards.Remove(cardVm);
            cardVm.Card.ColumnId = targetColumnId;
            targetColVm.Cards.Add(cardVm);
            cardVm.JustMoved = true;

            // Reset the flash flag after 1.5 s
            var timer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += (_, _) => { cardVm.JustMoved = false; timer.Stop(); };
            timer.Start();

            OnPropertyChanged(nameof(KanbanCardCount));
        });
    }

    /// <summary>Adds a card to the specified column.</summary>
    public async Task AddCardAsync(string title, string columnId)
    {
        if (_kanbanService is null || KanbanBoard is null) return;
        var card = new KanbanCard
        {
            BoardId  = KanbanBoard.Id,
            ColumnId = columnId,
            Title    = title,
            SortOrder = KanbanColumnViewModels
                .FirstOrDefault(c => c.Id == columnId)?.Cards.Count ?? 0
        };
        var created = await _kanbanService.CreateCardAsync(card).ConfigureAwait(false);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var colVm = KanbanColumnViewModels.FirstOrDefault(c => c.Id == columnId);
            colVm?.Cards.Add(new KanbanCardViewModel(created));
            OnPropertyChanged(nameof(KanbanCardCount));
        });
    }

    /// <summary>Saves all mutable fields of an existing card.</summary>
    public async Task SaveCardAsync(KanbanCard card)
    {
        if (_kanbanService is null) return;
        await _kanbanService.UpdateCardAsync(card).ConfigureAwait(false);
        // CardUpdated event will propagate changes to the card VM automatically
    }

    /// <summary>Deletes a card from the board.</summary>
    public async Task DeleteCardAsync(string cardId)
    {
        if (_kanbanService is null) return;
        await _kanbanService.DeleteCardAsync(cardId).ConfigureAwait(false);
        // CardDeleted event handler removes it from the column VM collection
    }

    /// <summary>Adds a new column to the board.</summary>
    public async Task AddColumnAsync(string title)
    {
        if (_kanbanService is null || KanbanBoard is null) return;
        var column = new KanbanColumn
        {
            BoardId   = KanbanBoard.Id,
            Title     = title,
            SortOrder = KanbanColumnViewModels.Count
        };
        var created = await _kanbanService.CreateColumnAsync(column).ConfigureAwait(false);
        KanbanBoard.Columns.Add(created);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var colVm = new KanbanColumnViewModel(created);
            colVm.Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(KanbanCardCount));
            KanbanColumnViewModels.Add(colVm);
        });
    }

    /// <summary>Persists a column title change after inline rename.</summary>
    public async Task UpdateColumnAsync(KanbanColumn column)
    {
        if (_kanbanService is null) return;
        await _kanbanService.UpdateColumnAsync(column).ConfigureAwait(false);
    }

    /// <summary>Deletes a column, moving its cards to the first remaining column.</summary>
    public async Task DeleteColumnAsync(string columnId)
    {
        if (_kanbanService is null || KanbanBoard is null) return;
        if (KanbanColumnViewModels.Count <= 1) return;  // guard: never delete last column

        await _kanbanService.DeleteColumnAsync(columnId, KanbanBoard.Id).ConfigureAwait(false);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var toRemove = KanbanColumnViewModels.FirstOrDefault(c => c.Id == columnId);
            if (toRemove is null) return;

            // Move orphaned card VMs to the first remaining column
            var fallback = KanbanColumnViewModels.FirstOrDefault(c => c.Id != columnId);
            if (fallback is not null)
                foreach (var cardVm in toRemove.Cards)
                {
                    cardVm.Card.ColumnId = fallback.Id;
                    fallback.Cards.Add(cardVm);
                }

            KanbanColumnViewModels.Remove(toRemove);
            KanbanBoard.Columns.RemoveAll(c => c.Id == columnId);
            OnPropertyChanged(nameof(KanbanCardCount));
        });
    }

    /// <summary>
    /// Checks dependencies and dispatches the card to an AI agent via TaskAutomationService.
    /// Shows a notification on error.
    /// </summary>
    public async Task ExecuteCardAsync(string cardId)
    {
        if (_taskAutomationService is null || KanbanBoard is null) return;

        try
        {
            // Working directory: not available from workspace model — pass null
            string? workDir = null;

            await _taskAutomationService.LaunchCardAsync(KanbanBoard.Id, cardId, workDir)
                .ConfigureAwait(false);

            // The service fires CardUpdated which will move the card to "running" via event handler
        }
        catch (InvalidOperationException ex)
        {
            _notificationService?.Notify(
                "Dependências não atendidas",
                NotificationType.Warning,
                NotificationSource.Terminal,
                ex.Message);
        }
        catch (Exception ex)
        {
            _notificationService?.Notify(
                "Erro ao executar tarefa",
                NotificationType.Error,
                NotificationSource.Terminal,
                ex.Message);
        }
    }

    // ─── Kanban event handlers (fired by IKanbanService) ─────────────────────

    private void OnKanbanCardUpdated(KanbanCard updated)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            // Find the card VM (may now be in a different column than updated.ColumnId)
            KanbanCardViewModel? cardVm = null;
            KanbanColumnViewModel? currentColVm = null;
            foreach (var col in KanbanColumnViewModels)
            {
                cardVm = col.Cards.FirstOrDefault(c => c.Id == updated.Id);
                if (cardVm is not null) { currentColVm = col; break; }
            }

            if (cardVm is null)
            {
                // Card not yet in any column VM — add it to the correct one
                var destColVm = KanbanColumnViewModels.FirstOrDefault(c => c.Id == updated.ColumnId);
                destColVm?.Cards.Add(new KanbanCardViewModel(updated));
                OnPropertyChanged(nameof(KanbanCardCount));
                return;
            }

            // Update card data in place
            cardVm.UpdateFrom(updated);

            // If the column changed, migrate the VM to the correct column
            if (currentColVm?.Id != updated.ColumnId)
            {
                currentColVm?.Cards.Remove(cardVm);
                var destColVm = KanbanColumnViewModels.FirstOrDefault(c => c.Id == updated.ColumnId);
                destColVm?.Cards.Add(cardVm);
                OnPropertyChanged(nameof(KanbanCardCount));
            }
        });
    }

    private void OnKanbanCardDeleted(string cardId)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var col in KanbanColumnViewModels)
            {
                var cardVm = col.Cards.FirstOrDefault(c => c.Id == cardId);
                if (cardVm is not null)
                {
                    col.Cards.Remove(cardVm);
                    OnPropertyChanged(nameof(KanbanCardCount));
                    return;
                }
            }
        });
    }

    // ─── System Monitor ───────────────────────────────────────────────────────

    private void StartSystemMonitor()
    {
        try
        {
            _cpuCounter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // First call always returns 0
        }
        catch { /* PerformanceCounter may not be available */ }

        _sysMonTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _sysMonTimer.Tick += OnSysMonTick;
        _sysMonTimer.Start();
    }

    private void OnSysMonTick(object? sender, EventArgs e)
    {
        try
        {
            if (_cpuCounter is not null)
                CpuUsage = Math.Round(_cpuCounter.NextValue(), 1);

            var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                var totalKb = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                var freeKb = Convert.ToDouble(obj["FreePhysicalMemory"]);
                MemoryTotalGb = Math.Round(totalKb / 1_048_576.0, 1);
                MemoryUsedGb = Math.Round((totalKb - freeKb) / 1_048_576.0, 1);
            }
        }
        catch { /* Silently skip on error */ }
    }

    private void StopSystemMonitor()
    {
        _sysMonTimer?.Stop();
        _sysMonTimer = null;
        _cpuCounter?.Dispose();
        _cpuCounter = null;
    }

    // ─── Chat operations ──────────────────────────────────────────────────────

    public async Task SendChatMessageAsync()
    {
        if (_assistantService is null || string.IsNullOrWhiteSpace(ChatInputText) || ChatIsLoading) return;

        var userText = ChatInputText.Trim();
        ChatInputText = string.Empty;
        ChatIsLoading = true;

        ChatMessages.Add(new ChatMessage { Role = "user", Content = userText, Timestamp = DateTime.Now });

        var assistantMsg = new ChatMessage { Role = "assistant", Content = string.Empty, Timestamp = DateTime.Now };
        ChatMessages.Add(assistantMsg);

        _chatCts = new CancellationTokenSource();
        try
        {
            var history = ChatMessages
                .Where(m => m.Role != "assistant" || m != assistantMsg)
                .Select(m => m.Role == "user"
                    ? AssistantMessage.User(m.Content)
                    : AssistantMessage.Assistant(m.Content))
                .ToList();

            await foreach (var chunk in _assistantService.StreamChatAsync(history, _chatCts.Token)
                               .ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                    assistantMsg.Content += chunk.Content;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            assistantMsg.Content = $"Erro: {ex.Message}";
        }
        finally
        {
            ChatIsLoading = false;
            _chatCts?.Dispose();
            _chatCts = null;
        }
    }

    // ─── System Monitor computed ──────────────────────────────────────────────

    /// <summary>Memory usage as a 0–100 percentage for progress bar binding.</summary>
    public double MemoryPercent =>
        MemoryTotalGb > 0 ? Math.Round(MemoryUsedGb / MemoryTotalGb * 100.0, 1) : 0;

    partial void OnMemoryUsedGbChanged(double value) => OnPropertyChanged(nameof(MemoryPercent));
    partial void OnMemoryTotalGbChanged(double value) => OnPropertyChanged(nameof(MemoryPercent));

    // ─── Cleanup ──────────────────────────────────────────────────────────────

    public void Cleanup()
    {
        StopSystemMonitor();
        StopTokenCounter();
        StopPomodoro();
        _chatCts?.Cancel();
        _chatCts?.Dispose();
        if (WidgetType == WidgetType.Process && _processMonitorService is not null)
            _processMonitorService.ProcessesUpdated -= OnProcessesUpdated;
        if (WidgetType == WidgetType.Kanban && _kanbanService is not null)
        {
            _kanbanService.CardUpdated -= OnKanbanCardUpdated;
            _kanbanService.CardDeleted -= OnKanbanCardDeleted;
        }
    }
}
