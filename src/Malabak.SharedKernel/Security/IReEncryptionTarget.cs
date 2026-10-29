namespace Malabak.SharedKernel.Security;

/// <summary>
/// Implemented by each module that owns encrypted columns, so key rotation can re-encrypt its rows
/// without the rotation job touching that module's DbContext.
/// </summary>
public interface IReEncryptionTarget
{
    string Name { get; }

    /// <summary>
    /// Re-encrypts up to <paramref name="batchSize"/> values still under an old data key and saves them.
    /// Returns how many were re-encrypted; 0 means the target is fully on the current key.
    /// </summary>
    Task<int> ReEncryptBatchAsync(IFieldCipher cipher, int batchSize, CancellationToken cancellationToken = default);
}
