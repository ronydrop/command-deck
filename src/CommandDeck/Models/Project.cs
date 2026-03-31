using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandDeck.Models;

/// <summary>
/// Represents a development project with its configuration.
/// </summary>
public partial class Project : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private ShellType _defaultShell = ShellType.WSL;

    [ObservableProperty]
    private List<string> _startupCommands = new();

    [ObservableProperty]
    private string _color = "#7C3AED";

    [ObservableProperty]
    private string _icon = "\uE74E";

    [ObservableProperty]
    private DateTime _lastOpened = DateTime.MinValue;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private int _sortOrder = 0;

    [ObservableProperty]
    [property: JsonIgnore]
    private GitInfo? _gitInfo;

    /// <summary>
    /// Detected project type based on file scanning.
    /// </summary>
    [ObservableProperty]
    private ProjectType _projectType = ProjectType.Unknown;

    /// <summary>
    /// Creates a deep copy of this project.
    /// </summary>
    public Project Clone() => new()
    {
        Id = Id,
        Name = Name,
        Path = Path,
        DefaultShell = DefaultShell,
        StartupCommands = new List<string>(StartupCommands),
        Color = Color,
        Icon = Icon,
        LastOpened = LastOpened,
        IsFavorite = IsFavorite,
        SortOrder = SortOrder,
        ProjectType = ProjectType
    };
}

/// <summary>
/// Types of development projects that can be auto-detected.
/// </summary>
public enum ProjectType
{
    Unknown,
    Laravel,
    NodeJs,
    TypeScript,
    React,
    Vue,
    NextJs,
    DotNet,
    Python,
    Docker
}
