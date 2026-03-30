using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

public sealed class AiContextService : IAiContextService
{
    private readonly ITerminalSessionService _sessionService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IProjectService _projectService;
    private readonly IGitService _gitService;

    public AiContextService(
        ITerminalSessionService sessionService,
        IWorkspaceService workspaceService,
        IProjectService projectService,
        IGitService gitService)
    {
        _sessionService = sessionService;
        _workspaceService = workspaceService;
        _projectService = projectService;
        _gitService = gitService;
    }

    public async Task<AiTerminalContext?> GetActiveTerminalContextAsync()
    {
        var activeItem = _workspaceService.ActiveTerminal;
        if (activeItem?.Terminal?.Session is null)
            return null;

        return await GetTerminalContextAsync(activeItem.Terminal.Session.Id);
    }

    public async Task<AiTerminalContext?> GetTerminalContextAsync(string sessionId)
    {
        var sessionModel = _sessionService.GetSession(sessionId);
        if (sessionModel is null)
            return null;

        var lastCmd = sessionModel.CommandHistory.Count > 0
            ? sessionModel.CommandHistory[^1]
            : null;

        string? projectName = null;
        string? projectPath = null;
        var projectType = ProjectType.Unknown;
        string? gitBranch = null;

        if (!string.IsNullOrEmpty(sessionModel.ProjectId))
        {
            var project = await _projectService.GetProjectAsync(sessionModel.ProjectId);
            if (project is not null)
            {
                projectName = project.Name;
                projectPath = project.Path;
                projectType = project.ProjectType;
                gitBranch = project.GitInfo?.Branch;
            }
        }

        if (gitBranch is null && !string.IsNullOrEmpty(sessionModel.WorkingDirectory))
        {
            try
            {
                var gitInfo = await _gitService.GetGitInfoAsync(sessionModel.WorkingDirectory);
                gitBranch = gitInfo?.Branch;
            }
            catch { }
        }

        return new AiTerminalContext
        {
            SessionId = sessionId,
            ShellType = sessionModel.ShellType,
            WorkingDirectory = sessionModel.WorkingDirectory,
            ProjectName = projectName,
            ProjectPath = projectPath,
            ProjectType = projectType,
            GitBranch = gitBranch,
            LastCommand = lastCmd,
            RecentOutput = GetRecentOutput(sessionId, 40),
            SessionState = sessionModel.SessionState,
            AiSessionType = sessionModel.AiSessionType,
            AiModelUsed = sessionModel.AiModelUsed
        };
    }

    public async Task<string> BuildPromptAsync(AiPromptIntent intent, AiTerminalContext? context = null, int outputLines = 40)
    {
        context ??= await GetActiveTerminalContextAsync();
        if (context is null)
            return string.Empty;

        var header = context.FormatForPrompt();
        var output = string.IsNullOrEmpty(context.RecentOutput)
            ? GetRecentOutput(context.SessionId, outputLines)
            : context.RecentOutput;

        return intent switch
        {
            AiPromptIntent.FixError => BuildFixErrorPrompt(header, output, context),
            AiPromptIntent.ExplainOutput => BuildExplainPrompt(header, output),
            AiPromptIntent.SuggestCommand => BuildSuggestPrompt(header, context),
            AiPromptIntent.SendContext => BuildSendContextPrompt(header, output),
            AiPromptIntent.GeneralQuestion => header,
            _ => header
        };
    }

    public string GetRecentOutput(string sessionId, int lines = 40)
    {
        var sessionModel = _sessionService.GetSession(sessionId);
        if (sessionModel is null)
            return string.Empty;

        var snapshot = sessionModel.OutputSnapshot;
        if (string.IsNullOrEmpty(snapshot))
            return string.Empty;

        var allLines = snapshot.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var taken = allLines.Length > lines ? allLines[^lines..] : allLines;
        return string.Join('\n', taken);
    }

    private static string BuildFixErrorPrompt(string header, string output, AiTerminalContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Fix the error below. Explain what went wrong and provide the corrected command or code.");
        sb.AppendLine();
        sb.AppendLine($"[Context] {header}");

        if (!string.IsNullOrEmpty(ctx.LastCommand))
        {
            sb.AppendLine();
            sb.AppendLine($"Command that failed: {ctx.LastCommand}");
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            sb.AppendLine();
            sb.AppendLine("Terminal output:");
            sb.AppendLine("```");
            sb.AppendLine(output);
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static string BuildExplainPrompt(string header, string output)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Explain the following terminal output concisely. If there are errors, explain the cause and how to fix them.");
        sb.AppendLine();
        sb.AppendLine($"[Context] {header}");

        if (!string.IsNullOrWhiteSpace(output))
        {
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(output);
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static string BuildSuggestPrompt(string header, AiTerminalContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Suggest the next shell command based on the current context. Reply with just the command and a brief explanation.");
        sb.AppendLine();
        sb.AppendLine($"[Context] {header}");

        if (!string.IsNullOrEmpty(ctx.LastCommand))
        {
            sb.AppendLine($"Last command: {ctx.LastCommand}");
        }

        return sb.ToString();
    }

    private static string BuildSendContextPrompt(string header, string output)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Context] {header}");

        if (!string.IsNullOrWhiteSpace(output))
        {
            sb.AppendLine();
            sb.AppendLine("Recent terminal output:");
            sb.AppendLine(output);
        }

        return sb.ToString();
    }
}
