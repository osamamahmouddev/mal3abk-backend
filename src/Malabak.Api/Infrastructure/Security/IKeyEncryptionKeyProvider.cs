namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// Wraps and unwraps data keys with a key-encryption key (KEK). In production this belongs in a KMS /
/// Key Vault so the KEK never leaves it; see ADR-0003.
/// </summary>
public interface IKeyEncryptionKeyProvider
{
    string CurrentKeyId { get; }

    byte[] WrapKey(byte[] dataKey);

    byte[] UnwrapKey(string keyEncryptionKeyId, byte[] wrappedKey);
}
