using System.Security.Cryptography;
using FluentAssertions;
using Malabak.Api.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure.Security;

public sealed class AwsKmsProviderTests
{
    private const string _kmsKey = "alias/malabak-kek";

    [Fact]
    public void WrapAndUnwrap_GoThroughKms_WithTheDekContext()
    {
        var kms = new FakeKms();
        var provider = Provider(kms, new EncryptionOptions { KeyProvider = "AwsKms", KmsKeyId = _kmsKey });
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var wrapped = provider.WrapKey(dataKey);

        provider.CurrentKeyId.Should().Be("aws-kms:" + _kmsKey);
        wrapped.Should().NotEqual(dataKey);
        provider.UnwrapKey(provider.CurrentKeyId, wrapped).Should().Equal(dataKey);
        kms.LastContext.Should().ContainKey("purpose").WhoseValue.Should().Be("malabak-dek");
    }

    [Fact]
    public void KeysWrappedByTheLocalKek_StillUnwrap_ForReWrapping()
    {
        var localKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var options = new EncryptionOptions
        {
            KeyProvider = "AwsKms",
            KmsKeyId = _kmsKey,
            CurrentKeyEncryptionKeyId = "local-1",
            KeyEncryptionKeys = { ["local-1"] = localKey },
            BlindIndexKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };
        var local = new LocalKeyEncryptionKeyProvider(Options.Create(options), NullLogger<LocalKeyEncryptionKeyProvider>.Instance);
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var wrappedLocally = local.WrapKey(dataKey);

        Provider(new FakeKms(), options).UnwrapKey("local-1", wrappedLocally).Should().Equal(dataKey);
    }

    [Fact]
    public void UnknownKek_IsRejected()
    {
        var provider = Provider(new FakeKms(), new EncryptionOptions { KeyProvider = "AwsKms", KmsKeyId = _kmsKey });

        var act = () => provider.UnwrapKey("local-1", [1, 2, 3]);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Validator_RequiresKmsKeyId_ButNotLocalKeys_InKmsMode()
    {
        var blind = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var validator = new EncryptionOptionsValidator();

        validator.Validate(null, new EncryptionOptions { KeyProvider = "AwsKms", KmsKeyId = _kmsKey, BlindIndexKey = blind }).Succeeded.Should().BeTrue();
        validator.Validate(null, new EncryptionOptions { KeyProvider = "AwsKms", BlindIndexKey = blind }).Failed.Should().BeTrue();
    }

    private static AwsKmsKeyEncryptionKeyProvider Provider(IKmsClient kms, EncryptionOptions options) =>
        new(kms, Options.Create(options), NullLoggerFactory.Instance);

    /// <summary>Stands in for KMS: seals with a key only it holds and enforces the encryption context.</summary>
    private sealed class FakeKms : IKmsClient
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public IReadOnlyDictionary<string, string>? LastContext { get; private set; }

        public Task<byte[]> EncryptAsync(string keyId, byte[] plaintext, IReadOnlyDictionary<string, string> context, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[plaintext.Length];
            using var aes = new AesGcm(_key, 16);
            aes.Encrypt(nonce, plaintext, cipher, tag, AssociatedData(keyId, context));
            return Task.FromResult<byte[]>([.. nonce, .. tag, .. cipher]);
        }

        public Task<byte[]> DecryptAsync(string keyId, byte[] ciphertext, IReadOnlyDictionary<string, string> context, CancellationToken cancellationToken = default)
        {
            var plain = new byte[ciphertext.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(ciphertext[..12], ciphertext[28..], ciphertext[12..28], plain, AssociatedData(keyId, context));
            return Task.FromResult(plain);
        }

        private static byte[] AssociatedData(string keyId, IReadOnlyDictionary<string, string> context) =>
            System.Text.Encoding.UTF8.GetBytes(keyId + "|" + string.Join(",", context.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
    }
}
