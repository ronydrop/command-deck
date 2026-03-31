using System.Text.Json;

namespace CommandDeck.Services.Browser;

public interface ICdpService
{
    bool IsAvailable { get; }
    Task InitializeAsync(IBrowserRuntimeService browserRuntime);
    Task<JsonDocument> CallMethodAsync(string method, string paramsJson = "{}");
    Task EnableDomainAsync(string domain);
    IDisposable SubscribeEvent(string eventName, Action<JsonDocument> handler);
    Task<byte[]> CaptureScreenshotAsync(double? clipX = null, double? clipY = null, double? clipWidth = null, double? clipHeight = null);
    Task<List<string>> GetConsoleErrorsAsync(int limit = 20);
}
