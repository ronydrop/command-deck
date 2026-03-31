using System.Diagnostics;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Launches project folders in external editors (Cursor, VS Code) or Windows Explorer.
/// </summary>
public class ExternalEditorService : IExternalEditorService
{
    private readonly INotificationService _notificationService;

    public ExternalEditorService(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <inheritdoc />
    public void Open(string projectPath, ExternalEditor editor)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) return;

        try
        {
            var (fileName, arguments) = editor switch
            {
                ExternalEditor.Cursor => ("cursor", $"\"{projectPath}\""),
                ExternalEditor.VsCode => ("code", $"\"{projectPath}\""),
                ExternalEditor.Explorer => ("explorer.exe", $"\"{projectPath}\""),
                _ => throw new ArgumentOutOfRangeException(nameof(editor))
            };

            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            var editorName = editor switch
            {
                ExternalEditor.Cursor => "Cursor",
                ExternalEditor.VsCode => "VS Code",
                ExternalEditor.Explorer => "Explorer",
                _ => editor.ToString()
            };

            Debug.WriteLine($"[ExternalEditor] Failed to open {editorName}: {ex.Message}");
            _notificationService.Notify(
                $"{editorName} não encontrado",
                NotificationType.Error,
                NotificationSource.System,
                message: $"Verifique se o {editorName} está instalado e disponível no PATH.");
        }
    }

    /// <inheritdoc />
    public bool IsAvailable(ExternalEditor editor)
    {
        if (editor == ExternalEditor.Explorer) return true;

        var cmd = editor switch
        {
            ExternalEditor.Cursor => "cursor",
            ExternalEditor.VsCode => "code",
            _ => throw new ArgumentOutOfRangeException(nameof(editor))
        };

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = cmd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });

            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
