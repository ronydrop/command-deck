using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Helpers;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the Prompt Template picker panel (shown inside chat tiles
/// and via the AI Orb menu).
/// </summary>
public partial class PromptTemplatePickerViewModel : ObservableObject
{
    private readonly IPromptTemplateService _service;
    private readonly ChatTileRouter _router;

    public ObservableCollection<PromptTemplate> Templates { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    [ObservableProperty] private PromptTemplate? _selectedTemplate;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "Todos";
    [ObservableProperty] private bool _isPickerOpen;

    // Fields for the selected template
    public ObservableCollection<TemplateFieldViewModel> FieldVms { get; } = new();

    // Agent modes
    public ObservableCollection<AgentMode> Modes { get; } = new();
    [ObservableProperty] private AgentMode? _activeMode;

    public PromptTemplatePickerViewModel(IPromptTemplateService service, ChatTileRouter router)
    {
        _service = service;
        _router = router;
        _service.DataChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        Templates.Clear();
        var q = _service.Templates.AsEnumerable();

        if (SelectedCategory != "Todos")
            q = q.Where(t => t.Category == SelectedCategory);

        if (!string.IsNullOrWhiteSpace(FilterText))
            q = q.Where(t =>
                t.Title.Contains(FilterText, System.StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(FilterText, System.StringComparison.OrdinalIgnoreCase));

        foreach (var t in q) Templates.Add(t);

        Categories.Clear();
        Categories.Add("Todos");
        foreach (var c in _service.Templates.Select(t => t.Category).Distinct().OrderBy(x => x))
            Categories.Add(c);

        Modes.Clear();
        foreach (var m in _service.Modes) Modes.Add(m);
    }

    partial void OnFilterTextChanged(string value) => Refresh();
    partial void OnSelectedCategoryChanged(string value) => Refresh();

    partial void OnSelectedTemplateChanged(PromptTemplate? value)
    {
        FieldVms.Clear();
        if (value is null) return;
        foreach (var f in value.Fields)
            FieldVms.Add(new TemplateFieldViewModel(f));
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SendTemplate()
    {
        if (SelectedTemplate is null) return;

        var values = FieldVms.ToDictionary(f => f.Key, f => f.Value);
        var rendered = SelectedTemplate.Render(values);
        await _router.RouteMessageAsync(rendered, SelectedTemplate.AutoSend);

        IsPickerOpen = false;
        SelectedTemplate = null;
    }

    [RelayCommand]
    private void OpenPicker() => IsPickerOpen = true;

    [RelayCommand]
    private void ClosePicker() => IsPickerOpen = false;

    [RelayCommand]
    private void SelectMode(AgentMode? mode)
    {
        ActiveMode = mode;
    }

    /// <summary>Returns the system prompt for the active mode (or null if default).</summary>
    public string? GetActiveSystemPrompt()
        => ActiveMode?.Id == "builtin-default" ? null : ActiveMode?.SystemPrompt;
}

/// <summary>VM for a single dynamic field in a template form.</summary>
public partial class TemplateFieldViewModel : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public string Placeholder { get; }
    public bool IsRequired { get; }

    [ObservableProperty] private string _value;

    public TemplateFieldViewModel(PromptTemplateField field)
    {
        Key = field.Key;
        Label = field.Label;
        Placeholder = field.Placeholder;
        IsRequired = field.IsRequired;
        _value = field.DefaultValue;
    }
}
