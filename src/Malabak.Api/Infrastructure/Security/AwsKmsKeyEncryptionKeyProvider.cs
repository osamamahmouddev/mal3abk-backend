using System.Security.Cryptography;
using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>The two KMS operations envelope encryption needs, so the provider can be tested without AWS.</summary>
public interface IKmsClient
{
    Task<byte[]> EncryptAsync(string keyId, byte[] plaintext, IReadOnlyDictionary<string, string> context, CancellationToken cancellationToken = default);

    Task<byte[]> DecryptAsync(string keyId, byte[] ciphertext, IReadOnlyDictionary<string, string> context, CancellationToken cancellationToken = default);
}

public sealed class AwsKmsClient : IKmsClient
{
    private readonly IAmazonKeyManagementService _kms;

    public AwsKmsClient(IOptions<EncryptionOptions> options)
    {
        var region = options.Value.KmsRegion;
        _kms = string.IsNullOrWhiteSpace(region)
            ? new AmazonKeyManagementServiceClient()
            : new AmazonKeyManagementServiceClient(RegionEndpoint.GetBySystemName(region));
    }

    public async Task<byte[]> EncryptAsync(string keyId, byte[] plaintext, IReadOnlyDictionary<string, string> context, CancellationToken cancellationToken = default)
    {
        using var input = new MemoryStream(plaintext, writable: false);
        var response = await _kms.EncryptAsync(
            new EncryptRequest { KeyId = keyId, Plaintext = input, EncryptionContext = new Dictionary<string, string>(context) },
            cancellationToken);
        return response.CiphertextBlob.ToArray();
    }

    public async Task<byte[]> DecryptAsync(string keyId, byte[] ciphertext, IReadOnlyDictionary<string, string> context, CancellationToken cancellationToken = default)
    {
        using var input = new MemoryStream(ciphertext, writable: false);
        var response = await _kms.DecryptAsync(
            new DecryptRequest { KeyId = keyId, CiphertextBlob = input, EncryptionContext = new Dictionary<string, string>(context) },
            cancellationToken);
        return response.Plaintext.ToArray();
    }
}

/// <summary>
/// KEK held in AWS KMS (ADR-0003/0004): data keys are wrapped and unwrapped by KMS and the KEK never leaves it.
/// The encryption context binds every wrapped key to Malabak data keys. Keys wrapped earlier by a local KEK are
/// still unwrapped through <see cref="LocalKeyEncryptionKeyProvider"/>, so the rotation service can re-wrap them.
/// Unwrapped data keys are cached by the key ring, so the synchronous KMS calls here are rare.
/// </summary>
public sealed class AwsKmsKeyEncryptionKeyProvider : IKeyEncryptionKeyProvider
{
    public const string KeyIdPrefix = "aws-kms:";

    private static readonly IReadOnlyDictionary<string, string> _context =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["purpose"] = "malabak-dek" };

    private readonly IKmsClient _kms;
    private readonly string _kmsKeyId;
    private readonly LocalKeyEncryptionKeyProvider? _legacy;

    public AwsKmsKeyEncryptionKeyProvider(IKmsClient kms, IOptions<EncryptionOptions> options, ILoggerFactory loggerFactory)
    {
        _kms = kms;
        _kmsKeyId = options.Value.KmsKeyId!;
        CurrentKeyId = KeyIdPrefix + _kmsKeyId;
        _legacy = options.Value.KeyEncryptionKeys.Count > 0 ? new LocalKeyEncryptionKeyProvider(options, loggerFactory.CreateLogger<LocalKeyEncryptionKeyProvider>()) : null;
    }

    public string CurrentKeyId { get; }

    public byte[] WrapKey(byte[] dataKey)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        return Task.Run(() => _kms.EncryptAsync(_kmsKeyId, dataKey, _context)).GetAwaiter().GetResult();
    }

    public byte[] UnwrapKey(string keyEncryptionKeyId, byte[] wrappedKey)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);

        if (keyEncryptionKeyId.StartsWith(KeyIdPrefix, StringComparison.Ordinal))
        {
            var kmsKeyId = keyEncryptionKeyId[KeyIdPrefix.Length..];
            return Task.Run(() => _kms.DecryptAsync(kmsKeyId, wrappedKey, _context)).GetAwaiter().GetResult();
        }

        return _legacy?.UnwrapKey(keyEncryptionKeyId, wrappedKey)
            ?? throw new CryptographicException($"Key-encryption key '{keyEncryptionKeyId}' is not available.");
    }
}
