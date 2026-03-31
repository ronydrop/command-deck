using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DevWorkspaceHub.Models;

/// <summary>
/// Serializable workspace layout — persisted to JSON/SQLite.
/// Stores camera state, canvas item positions/metadata, and workspace-level settings.
/// Terminal processes are NOT persisted (ConPTY sessions don't survive app restart);
/// only positions and metadata are restored, then fresh sessions are created.
/// </summary>
public class WorkspaceModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Workspace";

    /// <summary>Hex color for the workspace badge, e.g. "#CBA6F7".</summary>
    public string Color { get; set; } = "#CBA6F7";

    /// <summary>Icon key referencing Icons.xaml geometry, e.g. "FolderIcon".</summary>
    public string Icon { get; set; } = "FolderIcon";

    /// <summary>Whether this is the currently active workspace.</summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Active layout mode for this workspace (FreeCanvas or Tiled).</summary>
    public LayoutMode LayoutMode { get; set; } = LayoutMode.FreeCanvas;

    public CameraStateModel Camera { get; set; } = new();
    public List<CanvasItemModel> Items { get; set; } = new();

    /// <summary>Per-workspace settings (default shell, env vars, startup scripts, etc.).</summary>
    public WorkspaceSettings Settings { get; set; } = new();
}

/// <summary>
/// Per-workspace configuration. Overrides global settings when non-null.
/// </summary>
public class WorkspaceSettings
{
    /// <summary>Default shell type for new terminals in this workspace.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ShellType? DefaultShell { get; set; }

    /// <summary>Commands to execute when the workspace is activated.</summary>
    public List<string> StartupCommands { get; set; } = new();

    /// <summary>Commands to execute when the workspace is deactivated.</summary>
    public List<string> ShutdownCommands { get; set; } = new();

    /// <summary>Environment variables injected into all terminals of this workspace.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
}
