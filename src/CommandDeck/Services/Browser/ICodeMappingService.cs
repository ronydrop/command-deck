using CommandDeck.Models.Browser;

namespace CommandDeck.Services.Browser;

public interface ICodeMappingService
{
    Task<CodeMappingResult> MapElementToCodeAsync(ElementCaptureData element, string projectPath);
}
