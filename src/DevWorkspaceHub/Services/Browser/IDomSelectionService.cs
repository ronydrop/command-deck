using DevWorkspaceHub.Models.Browser;

namespace DevWorkspaceHub.Services.Browser;

public interface IDomSelectionService
{
    bool IsPickerActive { get; }

    event Action<ElementCaptureData>? ElementSelected;
    event Action? PickerActivated;
    event Action? PickerDeactivated;
    event Action? PickerCancelled;

    Task ActivatePickerAsync();
    Task DeactivatePickerAsync();
    Task TogglePickerAsync();
    void Initialize(IBrowserRuntimeService browserRuntime);
}
