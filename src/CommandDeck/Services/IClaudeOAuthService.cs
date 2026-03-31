using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Manages OAuth tokens from Claude Code's local credentials file.
/// </summary>
public interface IClaudeOAuthService
{
    /// <summary>Whether the Claude Code credentials file exists on this machine.</summary>
    bool IsClaudeCodeInstalled { get; }

    /// <summary>Load credentials from disk. Returns null if file is missing or unreadable.</summary>
    Task<ClaudeOAuthCredential?> LoadCredentialsAsync();

    /// <summary>Get a valid access token, refreshing if expired. Returns null on failure.</summary>
    Task<string?> GetValidAccessTokenAsync();

    /// <summary>Get the subscription type (Max/Pro) from cached credentials.</summary>
    string? GetSubscriptionType();
}
