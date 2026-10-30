using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// KEKs supplied through the secret store (User Secrets / environment / secret manager), for development and
/// tests. Production uses <see cref="AwsKmsKeyEncryptionKeyProvider"/> (ADR-0004), which still unwraps keys
/// wrapped here so they can be re-wrapped under KMS.
/// </summary>
public sealed class LocalKeyEncryptionKeyProvider : IKeyEncryptionKeyProvider
{
    private readonly Dictionary<string, byte[]> _keys;

    public LocalKeyEncryptionKeyProvider(IOptions<EncryptionOptions> options, ILogger<LocalKeyEncryptionKeyProvider> logger)
    {
        var opts = options.Value;
        CurrentKeyId = opts.CurrentKeyEncryptionKeyId;
        _keys = opts.KeyEncryptionKeys.ToDictionary(kv => kv.Key, kv => Convert.FromBase64String(kv.Value), StringComparer.Ordinal);

        if (opts.UsesEphemeralDevelopmentKeys)
        {
            logger.LogWarning(
                "Field encryption is using ephemeral development keys; encrypted data will be unreadable after restart. " +
                "Configure {Section} in User Secrets to persist keys.",
                EncryptionOptions.SectionName);
        }
    }

    public string CurrentKeyId { get; }

    public byte[] WrapKey(byte[] dataKey)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        return AesGcmSealer.Seal(_keys[CurrentKeyId], dataKey, AssociatedData(CurrentKeyId));
    }

    public byte[] UnwrapKey(string keyEncryptionKeyId, byte[] wrappedKey)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);

        if (!_keys.TryGetValue(keyEncryptionKeyId, out var kek))
        {
            throw new CryptographicException($"Key-encryption key '{keyEncryptionKeyId}' is not configured.");
        }

        return AesGcmSealer.Open(kek, wrappedKey, AssociatedData(keyEncryptionKeyId));
    }

    private static byte[] AssociatedData(string keyEncryptionKeyId) => Encoding.UTF8.GetBytes($"malabak-dek|{keyEncryptionKeyId}");
}
