using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandDeck.Models;

/// <summary>Status de detecção de uma ferramenta AI CLI.</summary>
public enum AiToolDetectionStatus
{
    Checking,
    Detected,
    NotFound
}

/// <summary>Representa uma ferramenta AI CLI com estado de detecção observável.</summary>
public partial class AiToolInfo : ObservableObject
{
    // Identidade imutável
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public string InstallCommand { get; init; } = string.Empty;
    public bool RequiresApiKey { get; init; }

    // Estado observável (mutável durante detecção)
    [ObservableProperty]
    private AiToolDetectionStatus _status = AiToolDetectionStatus.Checking;

    [ObservableProperty]
    private string _statusText = "Verificando...";
}
