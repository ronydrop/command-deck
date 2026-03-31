namespace CommandDeck.Services.Browser;

public interface ILocalAppSessionService
{
    Task<int?> DetectPortAsync(string projectPath, string? projectType = null);
    Task<bool> IsPortAvailableAsync(int port);
    string GetLocalUrl(int port);
}
