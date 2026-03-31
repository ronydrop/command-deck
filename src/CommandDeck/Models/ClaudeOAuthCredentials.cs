using System.Text.Json.Serialization;

namespace CommandDeck.Models;

/// <summary>
/// Root object for Claude Code's ~/.claude/.credentials.json file.
/// </summary>
public class ClaudeCredentialsFile
{
    [JsonPropertyName("claudeAiOauth")]
    public ClaudeOAuthCredential? ClaudeAiOauth { get; set; }
}

/// <summary>
/// OAuth credential from Claude Code's local session.
/// </summary>
public class ClaudeOAuthCredential
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Unix epoch in milliseconds when the access token expires.</summary>
    [JsonPropertyName("expiresAt")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("scopes")]
    public string[] Scopes { get; set; } = Array.Empty<string>();

    [JsonPropertyName("subscriptionType")]
    public string SubscriptionType { get; set; } = string.Empty;

    [JsonPropertyName("rateLimitTier")]
    public string RateLimitTier { get; set; } = string.Empty;
}
