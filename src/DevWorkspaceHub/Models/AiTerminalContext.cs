namespace DevWorkspaceHub.Models;

public sealed class AiTerminalContext
{
    public string SessionId { get; init; } = string.Empty;
    public ShellType ShellType { get; init; }
    public string WorkingDirectory { get; init; } = string.Empty;
    public string? ProjectName { get; init; }
    public string? ProjectPath { get; init; }
    public ProjectType ProjectType { get; init; } = ProjectType.Unknown;
    public string? GitBranch { get; init; }
    public string? LastCommand { get; init; }
    public string RecentOutput { get; init; } = string.Empty;
    public SessionState SessionState { get; init; }
    public AiSessionType AiSessionType { get; init; }
    public string AiModelUsed { get; init; } = string.Empty;

    public string FormatForPrompt()
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(WorkingDirectory))
            parts.Add($"CWD: {WorkingDirectory}");

        parts.Add($"Shell: {ShellType}");

        if (!string.IsNullOrEmpty(ProjectName))
            parts.Add($"Project: {ProjectName} ({ProjectType})");

        if (!string.IsNullOrEmpty(GitBranch))
            parts.Add($"Branch: {GitBranch}");

        if (!string.IsNullOrEmpty(LastCommand))
            parts.Add($"Last command: {LastCommand}");

        return string.Join(" | ", parts);
    }
}

public enum AiPromptIntent
{
    FixError,
    ExplainOutput,
    SuggestCommand,
    GeneralQuestion,
    SendContext
}
