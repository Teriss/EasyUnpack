using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyUnpack.Core.Passwords;

public static class PasswordVaultStore
{
    private const int Iterations = 600_000;
    private const int CurrentPayloadVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task SaveAsync(PasswordVault vault, string path, string? masterPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new PasswordVaultPayload(CurrentPayloadVersion, vault.Entries), JsonOptions);
        VaultEnvelope envelope = string.IsNullOrEmpty(masterPassword)
            ? VaultEnvelope.Plain(payload)
            : VaultEnvelope.Encrypted(payload, masterPassword);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(envelope, JsonOptions), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static async Task<PasswordVault> LoadAsync(string path, string? masterPassword, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new PasswordVault();

        var envelope = await ReadEnvelopeAsync(path, cancellationToken).ConfigureAwait(false);
        var payload = envelope.GetPayload(masterPassword);
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var legacyEntries = document.RootElement.Deserialize<List<PasswordEntry>>(JsonOptions) ?? [];
            return new PasswordVault(legacyEntries
                .OrderByDescending(static entry => entry.LastSuccessfulAt)
                .ThenByDescending(static entry => entry.SuccessCount));
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Password vault payload is invalid.");
        }

        var current = document.RootElement.Deserialize<PasswordVaultPayload>(JsonOptions)
            ?? throw new InvalidDataException("Password vault payload is invalid.");
        if (current.Version != CurrentPayloadVersion) throw new InvalidDataException($"Unsupported password vault payload version: {current.Version}.");
        return new PasswordVault(current.Entries);
    }

    public static async Task<PasswordVaultProtection> GetProtectionAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return PasswordVaultProtection.Plain;
        var envelope = await ReadEnvelopeAsync(path, cancellationToken).ConfigureAwait(false);
        return string.Equals(envelope.Protection, "aes-gcm", StringComparison.Ordinal)
            ? PasswordVaultProtection.MasterPassword
            : PasswordVaultProtection.Plain;
    }

    private static async Task<VaultEnvelope> ReadEnvelopeAsync(string path, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<VaultEnvelope>(json, JsonOptions) ?? throw new InvalidDataException("Password vault is invalid.");
    }

    private sealed record VaultEnvelope(string Protection, string Payload, string? Salt = null, string? Nonce = null, string? Tag = null)
    {
        public static VaultEnvelope Plain(byte[] payload) => new("plain", Convert.ToBase64String(payload));

        public static VaultEnvelope Encrypted(byte[] payload, string masterPassword)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var key = Rfc2898DeriveBytes.Pbkdf2(masterPassword, salt, Iterations, HashAlgorithmName.SHA256, 32);
            var ciphertext = new byte[payload.Length];
            var tag = new byte[16];
            try
            {
                using var aes = new AesGcm(key, tag.Length);
                aes.Encrypt(nonce, payload, ciphertext, tag);
                return new("aes-gcm", Convert.ToBase64String(ciphertext), Convert.ToBase64String(salt), Convert.ToBase64String(nonce), Convert.ToBase64String(tag));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        public byte[] GetPayload(string? masterPassword)
        {
            if (string.Equals(Protection, "plain", StringComparison.Ordinal)) return Convert.FromBase64String(Payload);
            if (!string.Equals(Protection, "aes-gcm", StringComparison.Ordinal) || string.IsNullOrEmpty(masterPassword) || Salt is null || Nonce is null || Tag is null)
            {
                throw new CryptographicException("A valid master password is required to open this password vault.");
            }

            var salt = Convert.FromBase64String(Salt);
            var nonce = Convert.FromBase64String(Nonce);
            var tag = Convert.FromBase64String(Tag);
            var ciphertext = Convert.FromBase64String(Payload);
            var plaintext = new byte[ciphertext.Length];
            var key = Rfc2898DeriveBytes.Pbkdf2(masterPassword, salt, Iterations, HashAlgorithmName.SHA256, 32);
            try
            {
                using var aes = new AesGcm(key, tag.Length);
                try
                {
                    aes.Decrypt(nonce, ciphertext, tag, plaintext);
                }
                catch (AuthenticationTagMismatchException exception)
                {
                    throw new CryptographicException("The master password is incorrect or the password vault is damaged.", exception);
                }
                return plaintext;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    private sealed record PasswordVaultPayload(int Version, IReadOnlyList<PasswordEntry> Entries);
}
