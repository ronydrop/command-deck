namespace CommandDeck.Services;

/// <summary>
/// Abstraction for user-facing dialogs. Allows ViewModels to trigger dialogs
/// without direct coupling to WPF UI types, maintaining MVVM separation.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a Yes/No confirmation dialog. Returns true if the user confirmed.
    /// </summary>
    Task<bool> ConfirmAsync(string message, string title = "Confirmar");

    /// <summary>
    /// Opens a folder browser dialog. Returns the selected path, or null if cancelled.
    /// </summary>
    Task<string?> BrowseFolderAsync(string title = "Selecionar Pasta");
}
