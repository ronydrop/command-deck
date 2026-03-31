using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DevWorkspaceHub.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceNodeType
{
    Workspace,
    Group,
    Project,
    Terminal
}

/// <summary>
/// A single node in the workspace hierarchy tree.
/// Groups contain projects and terminals; projects contain terminals.
/// </summary>
public class WorkspaceNodeModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public WorkspaceNodeType NodeType { get; set; } = WorkspaceNodeType.Group;
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex color for the node badge, e.g. "#7C3AED".</summary>
    public string Color { get; set; } = "#6C7086";

    /// <summary>Icon key referencing Icons.xaml geometry, e.g. \"FolderIcon\".</summary>
    public string IconKey { get; set; } = "FolderIcon";

    /// <summary>Alias for IconKey, used by WSL modules.</summary>
    [JsonIgnore]
    public string Icon
    {
        get => IconKey;
        set => IconKey = value;
    }

    /// <summary>Secondary text shown under the node name (e.g. path, status).</summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Parent node id. Null means root.</summary>
    public string? ParentId { get; set; }

    /// <summary>Typed back-link to parent node. Populated at runtime, not serialized.</summary>
    [JsonIgnore]
    public WorkspaceNodeModel? Parent { get; set; }

    /// <summary>For Terminal nodes: the CanvasItemModel.Id of the linked card.</summary>
    public string? LinkedCanvasItemId { get; set; }

    /// <summary>Alias for LinkedCanvasItemId, used by WSL modules.</summary>
    [JsonIgnore]
    public string? CanvasItemId
    {
        get => LinkedCanvasItemId;
        set => LinkedCanvasItemId = value;
    }

    /// <summary>Id of the workspace this node belongs to.</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>For Project nodes: the path on disk.</summary>
    public string? ProjectPath { get; set; }

    /// <summary>Sort order within parent.</summary>
    public int SortOrder { get; set; }

    /// <summary>For Terminal nodes: the terminal session id.</summary>
    public string? SessionId { get; set; }

    /// <summary>For Terminal nodes: the associated project id.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Whether this node can accept child nodes via drag-and-drop.</summary>
    [JsonIgnore]
    public bool CanAcceptChildren => NodeType is WorkspaceNodeType.Workspace or WorkspaceNodeType.Group or WorkspaceNodeType.Project;

    public List<WorkspaceNodeModel> Children { get; set; } = new();
}
