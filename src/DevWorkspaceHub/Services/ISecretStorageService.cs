namespace DevWorkspaceHub.Services;

/// <summary>
/// Service for securely storing and retrieving sensitive data (API keys,
/// tokens, passwords) using DPAPI encryption on Windows.
/// </summary>
public interface ISecretStorageService : IDisposable
{
    /// <summary>Stores a secret encrypted via DPAPI.</summary>
    /// <param name="key">Unique identifier for the secret (e.g. "github_token").</param>
    /// <param name="value">The plaintext secret value.</param>
    Task StoreSecretAsync(string key, string value);

    /// <summary>
    /// Retrieves a secret by key. Returns null if the key doesn't exist
    /// or if decryption fails.
    /// </summary>
    Task<string?> RetrieveSecretAsync(string key);

    /// <summary>Deletes a secret by key. Returns true if it existed.</summary>
    Task<bool> DeleteSecretAsync(string key);

    /// <summary>Returns all stored secret keys (values not included).</summary>
    Task<IReadOnlyList<string>> ListSecretKeysAsync();

    /// <summary>Checks if a secret exists for the given key.</summary>
    Task<bool> SecretExistsAsync(string key);
}
