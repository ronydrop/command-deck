using System.Threading.Tasks;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Checks for application updates via GitHub Releases.
/// </summary>
public interface IUpdateService
{
    /// <summary>Current application version.</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Checks GitHub for a newer release.
    /// Returns (hasUpdate, latestVersion, releaseUrl) or (false, current, null) if up-to-date.
    /// </summary>
    Task<UpdateCheckResult> CheckForUpdateAsync();
}

/// <summary>
/// Result of an update check.
/// </summary>
public record UpdateCheckResult(bool HasUpdate, string LatestVersion, string? ReleaseUrl, string? ReleaseNotes);
