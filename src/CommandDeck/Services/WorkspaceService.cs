using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Services;

/// <inheritdoc />
public class WorkspaceService : IWorkspaceService, ICanvasItemsService, IWorkspaceLifecycleService
{
    private readonly CanvasItemFactory _factory;
    private readonly IPersistenceService _persistence;
    private int _nextZIndex = 1;

    // Side-by-side layout: terminals placed left→right, wrap to next row when too wide
    private double _nextX = 40;
    private double _nextY = 40;
    private double _currentRowHeight = 0;
    private const double LayoutPadding = 24;
    private const double LayoutMaxWidth = 1800; // wrap column after this X

    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private CancellationTokenSource? _autoSaveCts;

    public ObservableCollection<CanvasItemViewModel> Items { get; } = new();
    public ObservableCollection<TerminalCanvasItemViewModel> TerminalItems { get; } = new();

    public TerminalCanvasItemViewModel? ActiveTerminal { get; set; }
    public WorkspaceModel? CurrentWorkspace { get; private set; }

    public event Action? WorkspaceChanged;
    public event Action<WorkspaceModel>? ActiveWorkspaceChanged;

    public WorkspaceService(CanvasItemFactory factory, IPersistenceService persistence)
    {
        _factory = factory;
        _persistence = persistence;
    }

    // ─── Add ────────────────────────────────────────────────────────────────────

    public TerminalCanvasItemViewModel AddTerminalItem(TerminalViewModel terminal)
    {
        var (x, y) = NextCascadePosition();
        var item = _factory.CreateTerminalItem(terminal, x, y);
        item.ZIndex = _nextZIndex++;

        Items.Add(item);
        TerminalItems.Add(item);

        if (ActiveTerminal is null)
            ActiveTerminal = item;

        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
        return item;
    }

    public WidgetCanvasItemViewModel AddWidgetItem(WidgetType type)
    {
        var (x, y) = NextCascadePosition();
        var item = _factory.CreateWidgetItem(type, x, y);
        item.ZIndex = _nextZIndex++;

        Items.Add(item);
        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
        return item;
    }

    public ChatCanvasItemViewModel AddChatTile(double x = 40, double y = 40)
    {
        var (cx, cy) = x == 40 && y == 40 ? NextCascadePosition() : (x, y);
        var item = _factory.CreateChatTileItem(cx, cy);
        item.ZIndex = _nextZIndex++;

        Items.Add(item);
        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
        return item;
    }

    public CodeEditorCanvasItemViewModel AddCodeEditorTile(double x = 40, double y = 40)
    {
        var (cx, cy) = x == 40 && y == 40 ? NextCascadePosition() : (x, y);
        var item = _factory.CreateCodeEditorItem(cx, cy);
        item.ZIndex = _nextZIndex++;
        Items.Add(item);
        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
        return item;
    }

    public FileExplorerCanvasItemViewModel AddFileExplorerTile(double x = 40, double y = 40)
    {
        var (cx, cy) = x == 40 && y == 40 ? NextCascadePosition() : (x, y);
        var item = _factory.CreateFileExplorerItem(cx, cy);
        item.ZIndex = _nextZIndex++;
        Items.Add(item);
        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
        return item;
    }

    public BrowserCanvasItemViewModel AddBrowserTile(double x = 40, double y = 40)
    {
        var (cx, cy) = x == 40 && y == 40 ? NextCascadePosition() : (x, y);
        var item = _factory.CreateBrowserItem(cx, cy);
        item.ZIndex = _nextZIndex++;
        Items.Add(item);
        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
        return item;
    }

    public ActivityFeedCanvasItemViewModel AddActivityFeedTile(double x = 40, double y = 40)
    {
        var (cx, cy) = x == 40 && y == 40 ? NextCascadePosition() : (x, y);
        var item = _factory.CreateActivityFeedItem(cx, cy);
        item.ZIndex = _nextZIndex++;
        Items.Add(item);
        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
        return item;
    }

    public bool HasWidget(WidgetType type)
        => Items.OfType<WidgetCanvasItemViewModel>().Any(w => w.WidgetType == type);

    public void ToggleWidgetItem(WidgetType type, double? fixedX = null, double? fixedY = null)
    {
        var existing = Items.OfType<WidgetCanvasItemViewModel>()
            .FirstOrDefault(w => w.WidgetType == type);

        if (existing is not null)
        {
            RemoveItem(existing.Model.Id);
            return;
        }

        if (fixedX.HasValue && fixedY.HasValue)
        {
            var item = _factory.CreateWidgetItem(type, fixedX.Value, fixedY.Value);
            item.ZIndex = _nextZIndex++;
            Items.Add(item);
            WorkspaceChanged?.Invoke();
            ScheduleAutoSave();
        }
        else
        {
            AddWidgetItem(type);
        }
    }

    // ─── Remove ─────────────────────────────────────────────────────────────────

    public void RemoveItem(string itemId)
    {
        var vm = Items.FirstOrDefault(i => i.Model.Id == itemId);
        if (vm is null) return;

        Items.Remove(vm);

        if (vm is TerminalCanvasItemViewModel tvm)
        {
            TerminalItems.Remove(tvm);
            if (ActiveTerminal?.Model.Id == itemId)
                ActiveTerminal = TerminalItems.FirstOrDefault();
        }

        // Dispose items that hold event subscriptions to prevent leaks
        if (vm is IDisposable disposable)
        {
            try { disposable.Dispose(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorkspaceService] Dispose failed for {vm.GetType().Name}: {ex.Message}");
            }
        }

        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
    }

    // ─── Move / Resize ──────────────────────────────────────────────────────────

    public void MoveItem(string itemId, double x, double y)
    {
        var vm = Items.FirstOrDefault(i => i.Model.Id == itemId);
        if (vm is null) return;
        vm.X = x;
        vm.Y = y;
        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
    }

    public void ResizeItem(string itemId, double width, double height)
    {
        var vm = Items.FirstOrDefault(i => i.Model.Id == itemId);
        if (vm is null) return;
        vm.Width = Math.Max(320, width);
        vm.Height = Math.Max(220, height);
        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
    }

    // ─── Z-Order ────────────────────────────────────────────────────────────────

    public void BringToFront(string itemId)
    {
        var vm = Items.FirstOrDefault(i => i.Model.Id == itemId);
        if (vm is null) return;
        vm.ZIndex = _nextZIndex++;
        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
    }

    // ─── Focus ──────────────────────────────────────────────────────────────────

    public void SetFocused(string? itemId)
    {
        foreach (var item in Items)
            item.IsFocused = item.Model.Id == itemId;
    }

    // ─── Clear / Restore ──────────────────────────────────────────────────────

    public void NotifyChanged()
    {
        WorkspaceChanged?.Invoke();
        ScheduleAutoSave();
    }

    public void ClearAll()
    {
        // Dispose terminal sessions (ConPTY) to avoid orphaned processes
        foreach (var terminal in TerminalItems.ToList())
        {
            try { terminal.Terminal?.Dispose(); }
            catch { /* best-effort cleanup */ }
        }

        // Dispose any other items that hold event subscriptions (ActivityFeed, etc.)
        foreach (var item in Items.OfType<IDisposable>().ToList())
        {
            try { item.Dispose(); }
            catch { /* best-effort cleanup */ }
        }

        Items.Clear();
        TerminalItems.Clear();
        ActiveTerminal = null;
        _nextX = 40;
        _nextY = 40;
        _nextZIndex = 1;
        _currentRowHeight = 0;
        WorkspaceChanged?.Invoke();
    }

    public void AddRestoredItem(CanvasItemViewModel item)
    {
        // Preserve original ZIndex from saved layout; track highest for new items
        if (item.ZIndex >= _nextZIndex)
            _nextZIndex = item.ZIndex + 1;

        Items.Add(item);

        if (item is TerminalCanvasItemViewModel tvm)
        {
            TerminalItems.Add(tvm);
            if (ActiveTerminal is null)
                ActiveTerminal = tvm;
        }

        WorkspaceChanged?.Invoke();
    }

    // ─── Multi-Workspace Lifecycle ──────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        // Try to load the active workspace from persistence
        var active = await _persistence.GetActiveWorkspaceAsync();
        if (active is null)
        {
            // No workspaces exist yet — create the default one
            active = await CreateWorkspaceAsync("Workspace Principal");
            await _persistence.SetActiveWorkspaceAsync(active.Id);
        }

        CurrentWorkspace = active;
        ActiveWorkspaceChanged?.Invoke(active);
    }

    public async Task<WorkspaceModel> CreateWorkspaceAsync(string name, string color = "#CBA6F7", string icon = "FolderIcon")
    {
        var workspace = new WorkspaceModel
        {
            Name = name,
            Color = color,
            Icon = icon,
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };

        await _persistence.SaveWorkspaceAsync(workspace);
        return workspace;
    }

    public async Task SwitchWorkspaceAsync(string workspaceId)
    {
        await _switchLock.WaitAsync();
        try
        {
            // Save current state before switching
            await SaveCurrentAsync();

            // Load the target workspace
            var target = await _persistence.LoadWorkspaceAsync(workspaceId);
            if (target is null)
                throw new InvalidOperationException($"Workspace '{workspaceId}' not found.");

            // Set it as active
            await _persistence.SetActiveWorkspaceAsync(workspaceId);
            target.IsActive = true;
            target.LastAccessedAt = DateTime.UtcNow;

            // Clear current canvas (disposes terminal sessions)
            ClearAll();

            // Update current reference
            CurrentWorkspace = target;
            ActiveWorkspaceChanged?.Invoke(target);
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async Task<IReadOnlyList<WorkspaceModel>> ListWorkspacesAsync()
    {
        return await _persistence.ListWorkspacesAsync();
    }

    public async Task<bool> DeleteWorkspaceAsync(string workspaceId)
    {
        if (CurrentWorkspace?.Id == workspaceId)
            throw new InvalidOperationException("Cannot delete the active workspace. Switch to another workspace first.");

        return await _persistence.DeleteWorkspaceAsync(workspaceId);
    }

    public async Task RenameWorkspaceAsync(string workspaceId, string newName)
    {
        var workspace = await _persistence.LoadWorkspaceAsync(workspaceId);
        if (workspace is null) return;

        workspace.Name = newName;
        await _persistence.SaveWorkspaceAsync(workspace);

        if (CurrentWorkspace?.Id == workspaceId)
        {
            CurrentWorkspace.Name = newName;
            ActiveWorkspaceChanged?.Invoke(CurrentWorkspace);
        }
    }

    public async Task UpdateWorkspaceColorAsync(string workspaceId, string newColor)
    {
        var workspace = await _persistence.LoadWorkspaceAsync(workspaceId);
        if (workspace is null) return;

        workspace.Color = newColor;
        await _persistence.SaveWorkspaceAsync(workspace);

        if (CurrentWorkspace?.Id == workspaceId)
        {
            CurrentWorkspace.Color = newColor;
            ActiveWorkspaceChanged?.Invoke(CurrentWorkspace);
        }
    }

    public async Task SaveCurrentAsync()
    {
        if (CurrentWorkspace is null) return;

        // Camera is kept up-to-date via UpdateCamera() calls from TerminalCanvasViewModel
        CurrentWorkspace.Items = Items.Select(i => i.Model).ToList();
        CurrentWorkspace.LastAccessedAt = DateTime.UtcNow;

        await _persistence.SaveWorkspaceAsync(CurrentWorkspace);
    }

    public void UpdateCamera(CameraStateModel camera)
    {
        if (CurrentWorkspace is not null)
            CurrentWorkspace.Camera = camera;
    }

    /// <summary>
    /// Schedules an auto-save after a short debounce period.
    /// Called internally after canvas mutations to prevent data loss on crash.
    /// </summary>
    private void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000, token);
                if (!token.IsCancellationRequested)
                    await SaveCurrentAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoSave] {ex.Message}");
            }
        }, token);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private (double x, double y) NextCascadePosition()
    {
        // Use the size of the item about to be placed (terminal default is 620×420).
        // Widgets vary, so fall back to their model size — but cascade is called before
        // the item is created, so we use the terminal default here; widgets land nearby.
        const double itemW = 780;
        const double itemH = 520;

        var result = (_nextX, _nextY);

        // Track tallest item in the current row for correct row wrapping
        if (itemH > _currentRowHeight) _currentRowHeight = itemH;

        // Advance to next column
        _nextX += itemW + LayoutPadding;

        // Wrap to next row when we'd overflow
        if (_nextX + itemW > LayoutMaxWidth)
        {
            _nextX = 40;
            _nextY += _currentRowHeight + LayoutPadding;
            _currentRowHeight = 0;
        }

        return result;
    }
}
