using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the tabbed terminal layout with optional split-pane support.
/// Delegates tab lifecycle to <see cref="TerminalManagerViewModel"/>.
/// </summary>
public partial class TabbedTerminalViewModel : ObservableObject
{
    private readonly TerminalManagerViewModel _terminalManager;

    // ─── Split pane state ────────────────────────────────────────────────────

    /// <summary>The second terminal shown in the split pane (null when not split).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSinglePane))]
    [NotifyPropertyChangedFor(nameof(IsSplitHorzVisible))]
    [NotifyPropertyChangedFor(nameof(IsSplitVertVisible))]
    private bool _isSplit;

    /// <summary>True = split horizontally (side-by-side), False = split vertically (top/bottom).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSplitHorzVisible))]
    [NotifyPropertyChangedFor(nameof(IsSplitVertVisible))]
    private bool _isSplitHorizontal = true;

    /// <summary>The second terminal shown in the split pane (null when not split).</summary>
    [ObservableProperty]
    private TerminalViewModel? _splitTerminal;

    // ─── Computed visibility helpers ─────────────────────────────────────────

    /// <summary>True when the view is in single-pane (non-split) mode.</summary>
    public bool IsSinglePane => !IsSplit;

    /// <summary>True when the horizontal split (side-by-side) pane should be shown.</summary>
    public bool IsSplitHorzVisible => IsSplit && IsSplitHorizontal;

    /// <summary>True when the vertical split (top/bottom) pane should be shown.</summary>
    public bool IsSplitVertVisible => IsSplit && !IsSplitHorizontal;

    // ─── Pass-through to TerminalManagerViewModel ─────────────────────────

    public TerminalManagerViewModel TerminalManager => _terminalManager;

    public TabbedTerminalViewModel(TerminalManagerViewModel terminalManager)
    {
        _terminalManager = terminalManager;
    }

    // ─── Tab commands ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new terminal tab without switching the current view away from TabbedTerminal.
    /// Unlike <c>MainViewModel.NewTerminalCommand</c>, this does NOT call <c>CurrentView = ViewType.Terminal</c>.
    /// </summary>
    [RelayCommand]
    private async Task NewTerminalInTabs()
    {
        await _terminalManager.NewTerminal();
    }

    /// <summary>
    /// Closes the given terminal without switching the current view.
    /// Unlike <c>MainViewModel.CloseTerminalCommand</c>, does not trigger a view navigation.
    /// </summary>
    [RelayCommand]
    private async Task CloseTerminalInTabs(TerminalViewModel? terminal)
    {
        if (terminal is null) return;

        // Also clear split terminal reference if the closed terminal was the split pane
        if (SplitTerminal == terminal)
        {
            SplitTerminal = null;
            IsSplit = false;
        }

        await _terminalManager.CloseTerminal(terminal);
    }

    // ─── Split commands ──────────────────────────────────────────────────────

    /// <summary>
    /// Splits the pane horizontally (active terminal on left, new terminal on right).
    /// </summary>
    [RelayCommand]
    private async Task SplitHorizontal()
    {
        IsSplitHorizontal = true;
        await OpenSplitAsync();
    }

    /// <summary>
    /// Splits the pane vertically (active terminal on top, new terminal on bottom).
    /// </summary>
    [RelayCommand]
    private async Task SplitVertical()
    {
        IsSplitHorizontal = false;
        await OpenSplitAsync();
    }

    /// <summary>
    /// Closes the split pane and returns to single-terminal view.
    /// </summary>
    [RelayCommand]
    private async Task CloseSplit()
    {
        if (SplitTerminal is not null)
            await _terminalManager.CloseTerminal(SplitTerminal);

        SplitTerminal = null;
        IsSplit = false;
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private async Task OpenSplitAsync()
    {
        if (IsSplit && SplitTerminal is not null) return; // already split

        // Create a new terminal that will occupy the split pane
        await _terminalManager.NewTerminal();
        var terminals = _terminalManager.Terminals;
        if (terminals.Count < 2) return;

        SplitTerminal = terminals[^1];
        IsSplit = true;

        // Keep focus on the original active terminal (the one before the new one)
        _terminalManager.ActiveTerminal = terminals[^2];
    }
}
