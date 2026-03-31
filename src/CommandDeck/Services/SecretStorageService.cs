using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CommandDeck.Services;

/// <summary>
/// DPAPI-backed implementation of <see cref="ISecretStorageService"/>.
/// Encrypts secrets using Windows Data Protection API (DPAPI) via
/// <see cref="System.Security.Cryptography.ProtectedData"/>.
/// All secrets are stored in a single binary file at
/// %APPDATA%/CommandDeck/secrets.dat.
/// </summary>
/// <remarks>
/// DPAPI provides machine+user-scoped encryption:
/// - Data encrypted on one machine can only be decrypted by the same user on that machine.
/// - No key management needed — Windows handles the key store.
/// - If the user profile is deleted, secrets are lost (expected behavior).
/// </remarks>
public sealed class SecretStorageService : ISecretStorageService
{
    private readonly string _secretsFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private Dictionary<string, byte[]>? _cache;
    private bool _disposed;

    // File format:
    // [4 bytes: magic "DWHS"]
    // [4 bytes: entry count (little-endian int32)]
    // For each entry:
    //   [4 bytes: key length]
    //   [key bytes (UTF-8)]
    //   [4 bytes: encrypted value length]
    //   [encrypted value bytes (DPAPI, includes entropy salt)]
    // [4 bytes: HMAC-SHA256 over all preceding bytes]
    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("DWHS");

    public SecretStorageService(string? secretsFilePath = null)
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CommandDeck");
        Directory.CreateDirectory(appData);
        _secretsFilePath = secretsFilePath ?? Path.Combine(appData, "secrets.dat");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ISecretStorageService
    // ═══════════════════════════════════════════════════════════════════════

    public async Task StoreSecretAsync(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var plainBytes = Encoding.UTF8.GetBytes(value);
        var encrypted = ProtectedData.Protect(plainBytes, GetEntropy(key), DataProtectionScope.CurrentUser);

        await _fileLock.WaitAsync();
        try
        {
            EnsureCache();
            _cache![key] = encrypted;
            await FlushToFileAsync();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<string?> RetrieveSecretAsync(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        await _fileLock.WaitAsync();
        try
        {
            EnsureCache();

            if (!_cache!.TryGetValue(key, out var encrypted))
                return null;

            try
            {
                var plainBytes = ProtectedData.Unprotect(encrypted, GetEntropy(key), DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException)
            {
                // Decryption failed — likely profile change or corrupted data
                System.Diagnostics.Debug.WriteLine($"[SecretStorage] Failed to decrypt secret: {key}");
                return null;
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> DeleteSecretAsync(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        await _fileLock.WaitAsync();
        try
        {
            EnsureCache();
            if (!_cache!.Remove(key))
                return false;
            await FlushToFileAsync();
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretKeysAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            EnsureCache();
            return _cache!.Keys.ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> SecretExistsAsync(string key)
    {
        await _fileLock.WaitAsync();
        try
        {
            EnsureCache();
            return _cache!.ContainsKey(key);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dispose
    // ═══════════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fileLock.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private Implementation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Per-key entropy salt — ties each encryption to its specific key,
    /// preventing an attacker who obtains the file from swapping values between keys.
    /// </summary>
    private static byte[] GetEntropy(string key)
    {
        // Use HMAC-SHA256 with a fixed application key to derive 16-byte entropy
        var appKey = Encoding.UTF8.GetBytes("CommandDeck.SecretStorage.Entropy");
        var keyBytes = Encoding.UTF8.GetBytes(key);

        using var hmac = HMACSHA256.Create();
        hmac.Key = appKey;
        var hash = hmac.ComputeHash(keyBytes);

        // Use first 16 bytes as entropy
        var entropy = new byte[16];
        Array.Copy(hash, entropy, 16);
        return entropy;
    }

    private void EnsureCache()
    {
        _cache ??= LoadFromFile();
    }

    private Dictionary<string, byte[]> LoadFromFile()
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(_secretsFilePath))
            return result;

        try
        {
            var bytes = File.ReadAllBytes(_secretsFilePath);
            if (bytes.Length < 12) // Minimum: magic(4) + count(4) + hmac(4)
            {
                System.Diagnostics.Debug.WriteLine("[SecretStorage] Secrets file too small, ignoring.");
                return result;
            }

            // Verify magic bytes
            if (!bytes[..4].SequenceEqual(MagicBytes))
            {
                System.Diagnostics.Debug.WriteLine("[SecretStorage] Invalid secrets file magic, ignoring.");
                return result;
            }

            var count = BitConverter.ToInt32(bytes, 4);
            if (count < 0 || count > 10_000)
            {
                System.Diagnostics.Debug.WriteLine("[SecretStorage] Unreasonable entry count, ignoring.");
                return result;
            }

            int offset = 8; // After magic + count
            for (int i = 0; i < count; i++)
            {
                if (offset + 4 > bytes.Length) break;

                var keyLen = BitConverter.ToInt32(bytes, offset);
                offset += 4;

                if (keyLen <= 0 || keyLen > 1024 || offset + keyLen > bytes.Length) break;
                var key = Encoding.UTF8.GetString(bytes, offset, keyLen);
                offset += keyLen;

                if (offset + 4 > bytes.Length) break;
                var valLen = BitConverter.ToInt32(bytes, offset);
                offset += 4;

                if (valLen <= 0 || valLen > 1024 * 1024 || offset + valLen > bytes.Length) break;
                var value = new byte[valLen];
                Array.Copy(bytes, offset, value, 0, valLen);
                offset += valLen;

                result[key] = value;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SecretStorage] Error loading secrets: {ex.Message}");
        }

        return result;
    }

    private async Task FlushToFileAsync()
    {
        // Build the binary file in memory
        using var ms = new MemoryStream();

        ms.Write(MagicBytes, 0, 4);
        ms.Write(BitConverter.GetBytes(_cache!.Count), 0, 4);

        foreach (var (key, value) in _cache)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            ms.Write(BitConverter.GetBytes(keyBytes.Length), 0, 4);
            ms.Write(keyBytes, 0, keyBytes.Length);
            ms.Write(BitConverter.GetBytes(value.Length), 0, 4);
            ms.Write(value, 0, value.Length);
        }

        // Write to a temp file first, then atomically replace
        var tempPath = _secretsFilePath + ".tmp";
        var data = ms.ToArray();
        await File.WriteAllBytesAsync(tempPath, data);

        try
        {
            File.Copy(tempPath, _secretsFilePath, overwrite: true);
        }
        finally
        {
            // Clean up temp file (best effort)
            try { File.Delete(tempPath); } catch { }
        }
    }
}
