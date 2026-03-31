using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

public interface IAiModelConfigService
{
    AiModelSlot ActiveSlot { get; }

    string GetModelForSlot(AiModelSlot slot);

    IReadOnlyList<AiModelConfig> GetAllSlots();

    void SetActiveSlot(AiModelSlot slot);

    void SetModelForSlot(AiModelSlot slot, string modelId);

    AiRoutingResult RecommendModel(AiPromptIntent intent);

    string GetActiveModelOrAlias();

    event Action? ConfigChanged;
}
