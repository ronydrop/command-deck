using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Reads and manages OAuth tokens from Claude Code's local credentials file.
/// Supports automatic token refresh when the access token expires.
/// </summary>
public sealed class ClaudeOAuthService : IClaudeOAuthService
{
    private static readonly string CredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    private const string TokenEndpoint = "https://api.anthropic.com/v1/oauth/token";
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private ClaudeOAuthCredential? _cached;

    public ClaudeOAuthService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public bool IsClaudeCodeInstalled => File.Exists(CredentialsPath);

    public async Task<ClaudeOAuthCredential?> LoadCredentialsAsync()
    {
        try
        {
            // Use FileShare.Read to avoid conflicts with Claude Code
            await using var stream = new FileStream(
                CredentialsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var file = await JsonSerializer.DeserializeAsync<ClaudeCredentialsFile>(stream);
            _cached = file?.ClaudeAiOauth;
            return _cached;
        }
        catch (Exception ex) when (ex is FileNotFoundException or JsonException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeOAuth] Failed to load credentials: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetValidAccessTokenAsync()
    {
        var creds = _cached ?? await LoadCredentialsAsync();
        if (creds is null)
            return null;

        if (!IsTokenExpired(creds))
            return creds.AccessToken;

        // Token expired or about to expire — try refresh
        await _refreshLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock (another thread may have refreshed)
            if (_cached is not null && !IsTokenExpired(_cached))
                return _cached.AccessToken;

            var refreshed = await RefreshTokenAsync(creds.RefreshToken);
            if (refreshed is not null)
            {
                _cached = refreshed;
                return refreshed.AccessToken;
            }

            // Refresh failed — return expired token and let the API return 401
            return creds.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public string? GetSubscriptionType()
    {
        return _cached?.SubscriptionType;
    }

    private static bool IsTokenExpired(ClaudeOAuthCredential credential)
    {
        var expiresAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(credential.ExpiresAt);
        return DateTimeOffset.UtcNow >= expiresAtUtc - RefreshBuffer;
    }

    private async Task<ClaudeOAuthCredential?> RefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = content
            };

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine(
                    $"[ClaudeOAuth] Refresh failed ({(int)response.StatusCode}): {errorBody[..Math.Min(200, errorBody.Length)]}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var newAccessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
            var newRefreshToken = root.TryGetProperty("refresh_token", out var rtEl)
                ? rtEl.GetString() ?? refreshToken
                : refreshToken;
            var expiresIn = root.TryGetProperty("expires_in", out var eiEl)
                ? eiEl.GetInt64()
                : 3600;

            var updated = new ClaudeOAuthCredential
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeMilliseconds(),
                Scopes = _cached?.Scopes ?? Array.Empty<string>(),
                SubscriptionType = _cached?.SubscriptionType ?? string.Empty,
                RateLimitTier = _cached?.RateLimitTier ?? string.Empty
            };

            // Write updated credentials back to file (atomic write)
            await WriteCredentialsAsync(updated);

            return updated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeOAuth] Refresh exception: {ex.Message}");
            return null;
        }
    }

    private static async Task WriteCredentialsAsync(ClaudeOAuthCredential credential)
    {
        try
        {
            var file = new ClaudeCredentialsFile { ClaudeAiOauth = credential };
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var tempPath = CredentialsPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, CredentialsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeOAuth] Failed to write credentials: {ex.Message}");
        }
    }
}
