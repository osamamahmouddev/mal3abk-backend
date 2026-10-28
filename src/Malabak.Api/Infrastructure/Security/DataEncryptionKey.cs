namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// A data key (DEK) stored only in wrapped form. The small integer id is embedded in every ciphertext
/// header so the right key can be found for decryption after rotation.
/// </summary>
public sealed class DataEncryptionKey
{
    private DataEncryptionKey()
    {
    }

    public int Id { get; }
    public byte[] WrappedKey { get; private set; } = [];
    public string KeyEncryptionKeyId { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public DateTime? RetiredAtUtc { get; private set; }
    public uint RowVersion { get; }

    public static DataEncryptionKey Create(byte[] wrappedKey, string keyEncryptionKeyId, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyEncryptionKeyId);

        return new DataEncryptionKey
        {
            WrappedKey = wrappedKey,
            KeyEncryptionKeyId = keyEncryptionKeyId,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }

    /// <summary>Stops using the key for new encryptions; it stays available for decryption.</summary>
    public void Retire(DateTime nowUtc)
    {
        IsActive = false;
        RetiredAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void Rewrap(byte[] wrappedKey, string keyEncryptionKeyId, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyEncryptionKeyId);

        WrappedKey = wrappedKey;
        KeyEncryptionKeyId = keyEncryptionKeyId;
        UpdatedAtUtc = nowUtc;
    }
}
