using System.Security.Cryptography;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>AES-256-GCM with a fresh random 96-bit nonce per call. Output layout: nonce | tag | ciphertext.</summary>
public static class AesGcmSealer
{
    public const int NonceSize = 12;
    public const int TagSize = 16;

    public static byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        var output = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = output.AsSpan(0, NonceSize);
        var tag = output.AsSpan(NonceSize, TagSize);
        var ciphertext = output.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return output;
    }

    /// <exception cref="CryptographicException">Authentication failed (wrong key, wrong AAD or tampered data).</exception>
    public static byte[] Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> associatedData)
    {
        if (sealedData.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Sealed data is truncated.");
        }

        var nonce = sealedData[..NonceSize];
        var tag = sealedData.Slice(NonceSize, TagSize);
        var ciphertext = sealedData[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }
}
