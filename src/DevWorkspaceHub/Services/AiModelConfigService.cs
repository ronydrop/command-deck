using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

public sealed class AiModelConfigService : IAiModelConfigService
{
    private readonly IPersistenceService _persistence;

    private AiModelSlot _activeSlot = AiModelSlot.Sonnet;

    private readonly Dictionary<AiModelSlot, AiModelConfig> _slots = new()
    {
        [AiModelSlot.Sonnet] = new AiModelConfig
        {
            Slot = AiModelSlot.Sonnet,
            ModelId = "sonnet",
            DisplayName = "Claude Sonnet",
            ShortLabel = "SNT",
            TaskAffinity = AiTaskAffinity.General
        },
        [AiModelSlot.Opus] = new AiModelConfig
        {
            Slot = AiModelSlot.Opus,
            ModelId = "opus",
            DisplayName = "Claude Opus",
            ShortLabel = "OPS",
            TaskAffinity = AiTaskAffinity.Refactoring
        },
        [AiModelSlot.Haiku] = new AiModelConfig
        {
            Slot = AiModelSlot.Haiku,
            ModelId = "haiku",
            DisplayName = "Claude Haiku",
            ShortLabel = "HKU",
            TaskAffinity = AiTaskAffinity.FastQuery
        },
        [AiModelSlot.Agent] = new AiModelConfig
        {
            Slot = AiModelSlot.Agent,
            ModelId = "agent",
            DisplayName = "Agent Mode",
            ShortLabel = "AGT",
            TaskAffinity = AiTaskAffinity.Agent
        }
    };

    public event Action? ConfigChanged;

    public AiModelConfigService(IPersistenceService persistence)
    {
        _persistence = persistence;
        _ = LoadFromPersistenceAsync();
    }

    public AiModelSlot ActiveSlot => _activeSlot;

    public string GetModelForSlot(AiModelSlot slot)
    {
        return _slots.TryGetValue(slot, out var cfg) ? cfg.ModelId : "sonnet";
    }

    public IReadOnlyList<AiModelConfig> GetAllSlots()
    {
        return _slots.Values.ToList().AsReadOnly();
    }

    public void SetActiveSlot(AiModelSlot slot)
    {
        if (_activeSlot == slot) return;
        _activeSlot = slot;
        ConfigChanged?.Invoke();
        _ = SaveToPersistenceAsync();
    }

    public void SetModelForSlot(AiModelSlot slot, string modelId)
    {
        if (!_slots.ContainsKey(slot)) return;

        var existing = _slots[slot];
        _slots[slot] = new AiModelConfig
        {
            Slot = slot,
            ModelId = modelId,
            DisplayName = existing.DisplayName,
            ShortLabel = existing.ShortLabel,
            TaskAffinity = existing.TaskAffinity
        };

        ConfigChanged?.Invoke();
        _ = SaveToPersistenceAsync();
    }

    public AiRoutingResult RecommendModel(AiPromptIntent intent)
    {
        var (slot, reason) = intent switch
        {
            AiPromptIntent.FixError => (AiModelSlot.Sonnet, "Error fixing works well with Sonnet's balance of speed and capability"),
            AiPromptIntent.ExplainOutput => (AiModelSlot.Haiku, "Quick explanations are fast enough for Haiku"),
            AiPromptIntent.SuggestCommand => (AiModelSlot.Haiku, "Command suggestions are straightforward, Haiku is sufficient"),
            AiPromptIntent.GeneralQuestion => (_activeSlot, "Using active slot for general questions"),
            AiPromptIntent.SendContext => (_activeSlot, "Using active slot for context-based queries"),
            _ => (_activeSlot, "Default to active slot")
        };

        return new AiRoutingResult
        {
            RecommendedSlot = slot,
            ModelOrAlias = GetModelForSlot(slot),
            Reason = reason
        };
    }

    public string GetActiveModelOrAlias()
    {
        return GetModelForSlot(_activeSlot);
    }

    private async Task LoadFromPersistenceAsync()
    {
        try
        {
            var data = await _persistence.LoadSettingAsync<AiModelPersistedConfig>("ai_model_config");
            if (data is null) return;

            if (Enum.TryParse<AiModelSlot>(data.ActiveSlot, true, out var slot))
                _activeSlot = slot;

            if (data.SlotModels is not null)
            {
                foreach (var (key, modelId) in data.SlotModels)
                {
                    if (Enum.TryParse<AiModelSlot>(key, true, out var s) && _slots.ContainsKey(s))
                        SetModelForSlotInternal(s, modelId);
                }
            }
        }
        catch { }
    }

    private async Task SaveToPersistenceAsync()
    {
        try
        {
            var data = new AiModelPersistedConfig
            {
                ActiveSlot = _activeSlot.ToString(),
                SlotModels = _slots.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => kv.Value.ModelId)
            };

            await _persistence.SaveSettingAsync("ai_model_config", data);
        }
        catch { }
    }

    private void SetModelForSlotInternal(AiModelSlot slot, string modelId)
    {
        if (!_slots.ContainsKey(slot)) return;
        var existing = _slots[slot];
        _slots[slot] = new AiModelConfig
        {
            Slot = slot,
            ModelId = modelId,
            DisplayName = existing.DisplayName,
            ShortLabel = existing.ShortLabel,
            TaskAffinity = existing.TaskAffinity
        };
    }

    private sealed class AiModelPersistedConfig
    {
        public string ActiveSlot { get; set; } = "Sonnet";
        public Dictionary<string, string>? SlotModels { get; set; }
    }
}
