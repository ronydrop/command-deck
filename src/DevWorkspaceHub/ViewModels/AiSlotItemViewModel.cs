using CommunityToolkit.Mvvm.ComponentModel;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// Sub-ViewModel representing a single AI model slot in the Settings UI.
/// </summary>
public partial class AiSlotItemViewModel : ObservableObject
{
    public AiModelSlot Slot { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string ShortLabel { get; init; } = string.Empty;

    [ObservableProperty]
    private string _modelId = string.Empty;

    [ObservableProperty]
    private bool _isActive;
}
