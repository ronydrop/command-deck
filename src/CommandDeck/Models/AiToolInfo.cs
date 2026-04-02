using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Threading.Tasks;

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
    public AiSessionType SessionType { get; init; } = AiSessionType.None;
    public string LaunchCommand { get; init; } = string.Empty;

    // Estado observável (mutável durante detecção)
    [ObservableProperty]
    private AiToolDetectionStatus _status = AiToolDetectionStatus.Checking;

    [ObservableProperty]
    private string _statusText = "Verificando...";

    /// <summary>True quando esta ferramenta é o default selecionado no dropdown.</summary>
    [ObservableProperty]
    private bool _isDefault;

    /// <summary>True briefly after copying the install command, for the check animation.</summary>
    [ObservableProperty]
    private bool _isCopied;

    [RelayCommand]
    private async Task CopyInstallCommand()
    {
        Clipboard.SetText(InstallCommand);
        IsCopied = true;
        await Task.Delay(2000);
        IsCopied = false;
    }
}
