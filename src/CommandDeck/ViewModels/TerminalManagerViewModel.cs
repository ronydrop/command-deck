using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// Manages the lifecycle of terminal tabs: creation, closure, active-tab tracking,
/// and workspace canvas registration.
/// Extracted from <see cref="MainViewModel"/> to keep navigation concerns separate.
/// </summary>
public partial class TerminalManagerViewModel : ObservableObject
{
    private readonly ITerminalService _terminalService;
    private readonly ISettingsService _settingsService;
    private readonly ICommandPaletteService _commandPaletteService;
    private readonly IWorkspaceService _workspaceService;
    private readonly INotificationService _notificationService;
    private readonly Func<TerminalViewModel> _terminalVmFactory;

    // ─── Observable Properties ───────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<TerminalViewModel> _terminals = new();

    [ObservableProperty]
    private TerminalViewModel? _activeTerminal;

    [ObservableProperty]
    private int _activeTerminalCount;

    [ObservableProperty]
    private string _shellTypeDisplay = string.Empty;

    /// <summary>
    /// Exposes the factory so callers (e.g. <see cref="MainViewModel"/> building a
    /// <see cref="ProjectSwitchContext"/>) can pass it to services without holding
    /// their own reference to it.
    /// </summary>
    internal Func<TerminalViewModel> TerminalVmFactory => _terminalVmFactory;

    // ─── Context (set externally by MainViewModel before creating terminals) ─

    /// <summary>The currently active project, used for shell type and working directory defaults.</summary>
    public Project? CurrentProject { get; set; }

    /// <summary>The active canvas ViewModel, used for canvas-wide focus operations on close.</summary>
    public TerminalCanvasViewModel? CanvasViewModel { get; set; }

    /// <summary>Fired when all terminal tabs have been closed.</summary>
    public event EventHandler? AllTerminalsClosed;

    public TerminalManagerViewModel(
        ITerminalService terminalService,
        Func<TerminalViewModel> terminalVmFactory,
        ISettingsService settingsService,
        ICommandPaletteService commandPaletteService,
        IWorkspaceService workspaceService,
        INotificationService notificationService)
    {
        _terminalService = terminalService;
        _terminalVmFactory = terminalVmFactory;
        _settingsService = settingsService;
        _commandPaletteService = commandPaletteService;
        _workspaceService = workspaceService;
        _notificationService = notificationService;
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    /// <summary>Creates a new terminal tab.</summary>
    [RelayCommand]
    public async Task NewTerminal()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            var shellType = CurrentProject?.DefaultShell ?? settings.DefaultShell;
            var workDir = CurrentProject?.Path;

            var terminalVm = _terminalVmFactory();
            Terminals.Add(terminalVm);
            ActiveTerminal = terminalVm;
            ActiveTerminalCount = Terminals.Count;

            await terminalVm.PrepareAsync(shellType, workDir, CurrentProject?.Id);

            // Register with workspace so it appears as a spatial canvas item
            _workspaceService.AddTerminalItem(terminalVm);

            ShellTypeDisplay = shellType.GetDisplayName();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NewTerminal] {ex}");
            _notificationService.Notify(
                "Erro ao criar terminal",
                NotificationType.Error,
                NotificationSource.System,
                message: ex.Message);
        }
    }

    /// <summary>Closes the specified terminal tab.</summary>
    [RelayCommand]
    public async Task CloseTerminal(TerminalViewModel? terminal)
    {
        if (terminal == null) return;

        // Remove from workspace canvas
        var canvasItem = _workspaceService.TerminalItems
            .FirstOrDefault(i => i.Terminal == terminal);
        if (canvasItem is not null)
            _workspaceService.RemoveItem(canvasItem.Model.Id);

        int index = Terminals.IndexOf(terminal);
        await terminal.DisposeAsync();
        Terminals.Remove(terminal);
        ActiveTerminalCount = Terminals.Count;

        // Select adjacent tab
        if (Terminals.Count > 0)
        {
            int newIndex = index >= 0 ? Math.Min(index, Terminals.Count - 1) : 0;
            ActiveTerminal = Terminals[newIndex];
        }
        else
        {
            ActiveTerminal = null;
            AllTerminalsClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Opens a new terminal and runs the given shell command in it.</summary>
    public async Task RunCommandInNewTerminalAsync(string command)
    {
        await NewTerminal();
        if (ActiveTerminal != null)
        {
            // Small delay to let the shell start
            await Task.Delay(500);
            await ActiveTerminal.ExecuteCommandAsync(command);
        }
    }

    // ─── Partial method handlers ─────────────────────────────────────────────

    partial void OnActiveTerminalChanged(TerminalViewModel? value)
    {
        // Deactivate all, activate selected
        foreach (var t in Terminals)
            t.IsActive = false;

        if (value != null)
        {
            value.IsActive = true;
            ShellTypeDisplay = value.ShellType.GetDisplayName();
        }
    }

    // ─── Internal helpers (called by MainViewModel during project switch) ────

    /// <summary>
    /// Resets collection state after a project switch completes.
    /// Must be called on the UI thread.
    /// </summary>
    internal void ApplySwitchResult(ProjectSwitchResult result)
    {
        Terminals.Clear();
        foreach (var vm in result.RestoredTerminals)
            Terminals.Add(vm);
        ActiveTerminal = result.ActiveTerminal ?? Terminals.FirstOrDefault();
        ActiveTerminalCount = Terminals.Count;
    }
}
