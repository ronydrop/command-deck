using System.Windows;
using Microsoft.Win32;

namespace CommandDeck.Services;

/// <summary>
/// WPF implementation of IDialogService.
/// All WPF-specific dialog calls are isolated here, away from ViewModels.
/// </summary>
public class DialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string message, string title = "Confirmar")
    {
        var result = MessageBox.Show(
            message, title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public Task<string?> BrowseFolderAsync(string title = "Selecionar Pasta")
    {
        var dialog = new OpenFolderDialog { Title = title };
        var selected = dialog.ShowDialog() == true ? dialog.FolderName : null;
        return Task.FromResult(selected);
    }
}
