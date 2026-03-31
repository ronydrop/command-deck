using System.ComponentModel;

namespace CommandDeck.Services;

/// <summary>
/// Supported external editors and file managers.
/// </summary>
public enum ExternalEditor
{
    [Description("Cursor")]
    Cursor,

    [Description("VS Code")]
    VsCode,

    [Description("Explorer")]
    Explorer
}

/// <summary>
/// Service for launching project folders in external editors or file managers.
/// </summary>
public interface IExternalEditorService
{
    /// <summary>
    /// Opens the given project path in the specified editor or file manager.
    /// </summary>
    void Open(string projectPath, ExternalEditor editor);

    /// <summary>
    /// Checks whether the given editor CLI is available on the system PATH.
    /// </summary>
    bool IsAvailable(ExternalEditor editor);
}
