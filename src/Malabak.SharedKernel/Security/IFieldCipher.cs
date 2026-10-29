namespace Malabak.SharedKernel.Security;

/// <summary>
/// Field-level encryption (AES-256-GCM, envelope keys) for sensitive columns such as payout account numbers.
/// Ciphertexts carry the id of the data key that produced them, so old rows stay readable after key rotation.
/// </summary>
public interface IFieldCipher
{
    ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default);

    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The ciphertext was tampered with, belongs to another row/column, or its key is unknown.
    /// </exception>
    ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default);

    /// <summary>True when the value was encrypted with a data key other than the current one.</summary>
    ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default);
}
