namespace DevWorkspaceHub.Models.Browser;

public class ElementContext
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;
    public required ElementCaptureData RawCapture { get; init; } = new();
    public string PageUrl { get; init; } = string.Empty;
    public string PageTitle { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string? ProjectPath { get; init; }
    public CodeMappingResult? CodeMapping { get; init; }
    public string? ScreenshotBase64 { get; init; }
    public List<string> ConsoleErrors { get; init; } = new();
    public bool WasSanitized { get; init; }
}

public class CodeMappingResult
{
    public string? FilePath { get; init; }
    public int? LineNumber { get; init; }
    public string? ComponentName { get; init; }
    public double Confidence { get; init; }
    public CodeMappingStrategy Strategy { get; init; }
    public List<CodeMappingCandidate> Candidates { get; init; } = new();
}

public enum CodeMappingStrategy
{
    ReactFiber,
    DataTestId,
    ClassNameHeuristic,
    FileSearch,
    None
}

public class CodeMappingCandidate
{
    public string FilePath { get; init; } = string.Empty;
    public int? LineNumber { get; init; }
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
}
