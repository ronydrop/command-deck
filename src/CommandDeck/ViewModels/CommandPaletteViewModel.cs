using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the Ctrl+K command palette overlay.
/// Holds the search query, filtered result list and keyboard navigation state.
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject, IDisposable
{
    private readonly ICommandPaletteService _paletteService;

    // ─── Original WIN properties ──────────────────────────────────────────

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private ObservableCollection<CommandDefinitionModel> _results = new();
    [ObservableProperty] private CommandDefinitionModel? _selectedResult;
    [ObservableProperty] private bool _isOpen;

    public event Action? CloseRequested;

    // ─── Expanded WSL properties ──────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private CommandDefinition? _selectedCommand;

    private ObservableCollection<CommandDefinition> _filteredCommands = new();

    /// <summary>Filtered commands displayed in the list (WSL-style).</summary>
    public ObservableCollection<CommandDefinition> FilteredCommands
    {
        get => _filteredCommands;
        private set => SetProperty(ref _filteredCommands, value);
    }

    /// <summary>Whether there are any search results to display.</summary>
    public bool HasResults => FilteredCommands.Count > 0;

    private bool _isSearching;

    public CommandPaletteViewModel(ICommandPaletteService paletteService)
    {
        _paletteService = paletteService;
        _paletteService.CommandsChanged += RefreshResults;
    }

    // ─── Original WIN Commands ────────────────────────────────────────────

    [RelayCommand]
    private void Open()
    {
        Query = string.Empty;
        SearchText = string.Empty;
        RefreshResults();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Confirm()
    {
        SelectedResult?.Execute();
        Close();
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (!Results.Any()) return;
        if (SelectedResult is null) { SelectedResult = Results.First(); return; }
        int i = Results.IndexOf(SelectedResult);
        if (i < Results.Count - 1) SelectedResult = Results[i + 1];
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (!Results.Any()) return;
        if (SelectedResult is null) { SelectedResult = Results.Last(); return; }
        int i = Results.IndexOf(SelectedResult);
        if (i > 0) SelectedResult = Results[i - 1];
    }

    // ─── Expanded WSL Commands ────────────────────────────────────────────

    /// <summary>
    /// Toggles the command palette open/closed.
    /// </summary>
    [RelayCommand]
    public void Toggle()
    {
        IsOpen = !IsOpen;
    }

    /// <summary>
    /// Moves selection up by one item (wraps around, WSL-style).
    /// </summary>
    [RelayCommand]
    public void NavigateUp()
    {
        if (FilteredCommands.Count == 0) return;
        SelectedIndex = SelectedIndex <= 0 ? FilteredCommands.Count - 1 : SelectedIndex - 1;
    }

    /// <summary>
    /// Moves selection down by one item (wraps around, WSL-style).
    /// </summary>
    [RelayCommand]
    public void NavigateDown()
    {
        if (FilteredCommands.Count == 0) return;
        SelectedIndex = SelectedIndex >= FilteredCommands.Count - 1 ? 0 : SelectedIndex + 1;
    }

    /// <summary>
    /// Executes the currently selected WSL-style command.
    /// </summary>
    [RelayCommand]
    public async Task ExecuteSelected()
    {
        if (SelectedCommand is null) return;
        await _paletteService.ExecuteCommandAsync(SelectedCommand);
        IsOpen = false;
    }

    // ─── Reactive search ──────────────────────────────────────────────────

    partial void OnQueryChanged(string value) => RefreshResults();

    partial void OnSearchTextChanged(string value) => UpdateFilteredCommands(value);

    partial void OnSelectedIndexChanged(int value)
    {
        if (FilteredCommands.Count == 0)
        {
            SelectedCommand = null;
            return;
        }
        if (value < 0) value = 0;
        if (value >= FilteredCommands.Count) value = FilteredCommands.Count - 1;
        if (value != SelectedIndex)
        {
            SelectedIndex = value;
            return;
        }
        SelectedCommand = FilteredCommands.Count > value ? FilteredCommands[value] : null;
    }

    private void RefreshResults()
    {
        var prev = SelectedResult?.Id;
        Results.Clear();
        foreach (var cmd in _paletteService.Search(Query))
            Results.Add(cmd);

        SelectedResult = Results.FirstOrDefault(c => c.Id == prev) ?? Results.FirstOrDefault();
    }

    private void UpdateFilteredCommands(string query)
    {
        if (_isSearching) return;
        _isSearching = true;

        try
        {
            var results = _paletteService.SearchCommands(query);
            FilteredCommands = new ObservableCollection<CommandDefinition>(results);
            SelectedIndex = FilteredCommands.Count > 0 ? 0 : -1;
        }
        finally
        {
            _isSearching = false;
        }
    }

    // ─── IDisposable ──────────────────────────────────────────────────────

    public void Dispose()
    {
        Results.Clear();
        FilteredCommands.Clear();
    }
}
