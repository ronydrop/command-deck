using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Checks for application updates via the GitHub Releases API.
/// </summary>
public class UpdateService : IUpdateService
{
    private const string GitHubOwner = "ronydrop";
    private const string GitHubRepo = "dev-workspace-hub";
    private static readonly string LatestReleaseUrl =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private readonly HttpClient _httpClient;

    public string CurrentVersion { get; }

    public UpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;

        // Read version from assembly InformationalVersion attribute
        var infoVersion = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        // Strip build metadata (e.g. "+sha.abc123") if present
        CurrentVersion = infoVersion?.Split('+')[0] ?? "1.0.0";
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.UserAgent.ParseAdd("DevWorkspaceHub-UpdateChecker");
            request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(false, CurrentVersion, null, null);

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var htmlUrl = root.GetProperty("html_url").GetString();
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;

            // Normalize: remove leading "v" from tag (e.g. "v1.2.0" -> "1.2.0")
            var latestVersionStr = tagName.TrimStart('v', 'V');

            if (Version.TryParse(latestVersionStr, out var latestVersion) &&
                Version.TryParse(CurrentVersion, out var currentVersion) &&
                latestVersion > currentVersion)
            {
                return new UpdateCheckResult(true, latestVersionStr, htmlUrl, body);
            }

            return new UpdateCheckResult(false, CurrentVersion, null, null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateService] Check failed: {ex.Message}");
            return new UpdateCheckResult(false, CurrentVersion, null, null);
        }
    }
}
