using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Malabak.SharedKernel.Security;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// Ciphertext layout: version (1) | data key id (4, big-endian) | nonce (12) | tag (16) | ciphertext.
/// The header (version + key id) is authenticated together with the field context as associated data,
/// so neither can be swapped without decryption failing.
/// </summary>
public sealed class AesGcmFieldCipher : IFieldCipher
{
    public const byte FormatVersion = 1;
    private const int _headerSize = 1 + sizeof(int);

    private readonly IDataKeyRing _keyRing;

    public AesGcmFieldCipher(IDataKeyRing keyRing)
    {
        _keyRing = keyRing;
    }

    public async ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(context);

        var dataKey = await _keyRing.GetCurrentAsync(cancellationToken);
        var header = BuildHeader(dataKey.Id);
        var sealedData = AesGcmSealer.Seal(dataKey.Key, Encoding.UTF8.GetBytes(plaintext), AssociatedData(header, context));

        return [.. header, .. sealedData];
    }

    public async ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var keyId = ReadKeyId(ciphertext);
        var key = await _keyRing.GetKeyAsync(keyId, cancellationToken);
        var header = ciphertext.AsSpan(0, _headerSize).ToArray();
        var plaintext = AesGcmSealer.Open(key, ciphertext.AsSpan(_headerSize), AssociatedData(header, context));

        return Encoding.UTF8.GetString(plaintext);
    }

    public async ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default)
    {
        var keyId = ReadKeyId(ciphertext);
        var current = await _keyRing.GetCurrentAsync(cancellationToken);
        return keyId != current.Id;
    }

    private static byte[] BuildHeader(int keyId)
    {
        var header = new byte[_headerSize];
        header[0] = FormatVersion;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), keyId);
        return header;
    }

    private static int ReadKeyId(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (ciphertext.Length < _headerSize + AesGcmSealer.NonceSize + AesGcmSealer.TagSize)
        {
            throw new CryptographicException("Ciphertext is truncated.");
        }

        if (ciphertext[0] != FormatVersion)
        {
            throw new CryptographicException("Unsupported ciphertext format version.");
        }

        return BinaryPrimitives.ReadInt32BigEndian(ciphertext.AsSpan(1, sizeof(int)));
    }

    private static byte[] AssociatedData(byte[] header, FieldEncryptionContext context) =>
        [.. header, .. Encoding.UTF8.GetBytes(context.ToAssociatedData())];
}
