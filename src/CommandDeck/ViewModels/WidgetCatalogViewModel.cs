using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the Widget Catalog settings tab.
/// Shows all built-in widgets with toggle on/off + metadata.
/// </summary>
public partial class WidgetCatalogViewModel : ObservableObject
{
    private readonly IWidgetCatalogService _catalog;

    public ObservableCollection<WidgetCatalogEntryViewModel> Entries { get; } = new();

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "Todos";

    public string[] Categories { get; } = ["Todos", "Core", "IA", "Dev", "Produtividade", "Sistema", "Conteúdo", "Database"];

    public WidgetCatalogViewModel(IWidgetCatalogService catalog)
    {
        _catalog = catalog;
        _catalog.CatalogChanged += RefreshEntries;
        RefreshEntries();
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        var entries = _catalog.All.AsEnumerable();

        if (SelectedCategory != "Todos")
            entries = entries.Where(e => e.Category == SelectedCategory);

        if (!string.IsNullOrWhiteSpace(FilterText))
            entries = entries.Where(e =>
                e.Name.Contains(FilterText, System.StringComparison.OrdinalIgnoreCase) ||
                e.Description.Contains(FilterText, System.StringComparison.OrdinalIgnoreCase));

        foreach (var e in entries)
            Entries.Add(new WidgetCatalogEntryViewModel(e, _catalog));
    }

    partial void OnFilterTextChanged(string value) => RefreshEntries();
    partial void OnSelectedCategoryChanged(string value) => RefreshEntries();

    [RelayCommand]
    private void ResetDefaults()
    {
        _catalog.ResetToDefaults();
        RefreshEntries();
    }

    [RelayCommand]
    private void EnableAll()
    {
        foreach (var e in _catalog.All.Where(x => !x.IsCore))
            _catalog.SetEnabled(e.Key, true);
    }
}

/// <summary>ViewModel wrapper for a single catalog entry card.</summary>
public partial class WidgetCatalogEntryViewModel : ObservableObject
{
    private readonly IWidgetCatalogService _catalog;
    public WidgetCatalogEntry Entry { get; }

    public string Key => Entry.Key;
    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Icon => Entry.Icon;
    public string AccentColor => Entry.AccentColor;
    public string Category => Entry.Category;
    public string PreviewHint => Entry.PreviewHint;
    public bool IsCore => Entry.IsCore;

    [ObservableProperty] private bool _isEnabled;

    public WidgetCatalogEntryViewModel(WidgetCatalogEntry entry, IWidgetCatalogService catalog)
    {
        Entry = entry;
        _catalog = catalog;
        _isEnabled = entry.IsEnabled;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (Entry.IsCore) return;
        Entry.IsEnabled = value;
        _catalog.SetEnabled(Entry.Key, value);
    }
}
