using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// Canvas item ViewModel for the Activity Feed widget.
/// Shows a real-time scrolling log of application events.
/// </summary>
public partial class ActivityFeedCanvasItemViewModel : CanvasItemViewModel
{
    private readonly IActivityFeedService _feed;

    public override CanvasItemType ItemType => CanvasItemType.ActivityFeedWidget;

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _selectedTypeFilter = "Todos";
    [ObservableProperty] private bool _isPaused;

    public ObservableCollection<ActivityEntry> Entries { get; } = new();

    public string[] TypeFilters { get; } = ["Todos", "Terminal", "Projeto", "Git", "IA", "Editor", "Browser", "Widget", "Sistema"];

    public ActivityFeedCanvasItemViewModel(CanvasItemModel model, IActivityFeedService feed)
        : base(model)
    {
        _feed = feed;
        _feed.EntryAdded += OnEntryAdded;
        LoadRecent();
    }

    private void LoadRecent()
    {
        var recent = _feed.GetRecent(100);
        foreach (var e in recent) Entries.Add(e);
    }

    private void OnEntryAdded(ActivityEntry entry)
    {
        if (IsPaused) return;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (!MatchesFilter(entry)) return;
            Entries.Insert(0, entry);
            // Keep max 200 in UI
            while (Entries.Count > 200) Entries.RemoveAt(Entries.Count - 1);
        });
    }

    private bool MatchesFilter(ActivityEntry entry)
    {
        if (SelectedTypeFilter != "Todos")
        {
            var typeMatch = SelectedTypeFilter switch
            {
                "Terminal" => entry.Type == ActivityEntryType.Terminal,
                "Projeto"  => entry.Type == ActivityEntryType.Project,
                "Git"      => entry.Type == ActivityEntryType.Git,
                "IA"       => entry.Type == ActivityEntryType.AI,
                "Editor"   => entry.Type == ActivityEntryType.Editor,
                "Browser"  => entry.Type == ActivityEntryType.Browser,
                "Widget"   => entry.Type == ActivityEntryType.Widget,
                "Sistema"  => entry.Type == ActivityEntryType.System,
                _ => true
            };
            if (!typeMatch) return false;
        }

        if (!string.IsNullOrWhiteSpace(FilterText) &&
            !entry.Title.Contains(FilterText, StringComparison.OrdinalIgnoreCase) &&
            !(entry.Detail?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        return true;
    }

    partial void OnFilterTextChanged(string value) => RefreshFilter();
    partial void OnSelectedTypeFilterChanged(string value) => RefreshFilter();

    private void RefreshFilter()
    {
        Entries.Clear();
        foreach (var e in _feed.GetRecent(200).Where(MatchesFilter))
            Entries.Add(e);
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private void ClearFeed()
    {
        _feed.Clear();
        Entries.Clear();
    }

    public void Dispose()
    {
        _feed.EntryAdded -= OnEntryAdded;
    }
}
