using DevWorkspaceHub.Models.Browser;

namespace DevWorkspaceHub.Services.Browser;

public interface ICodeMappingService
{
    Task<CodeMappingResult> MapElementToCodeAsync(ElementCaptureData element, string projectPath);
}
