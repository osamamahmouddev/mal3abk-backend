namespace Malabak.Api.Infrastructure.Security;

public interface IDataKeyRing
{
    /// <summary>The active data key, created on first use if none exists yet.</summary>
    ValueTask<DataKey> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <exception cref="System.Security.Cryptography.CryptographicException">No data key with this id exists.</exception>
    ValueTask<byte[]> GetKeyAsync(int keyId, CancellationToken cancellationToken = default);

    /// <summary>Retires the active data key and activates a new one. Returns the new key id.</summary>
    Task<int> RotateAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-wraps every data key still wrapped by a non-current KEK. Returns how many were re-wrapped.</summary>
    Task<int> RewrapWithCurrentKeyEncryptionKeyAsync(CancellationToken cancellationToken = default);
}

public readonly record struct DataKey(int Id, byte[] Key);
