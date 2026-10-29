namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// Key material for field-level encryption. Values come from User Secrets locally and from the secret
/// manager in staging/production — never from appsettings or the repository.
/// </summary>
public sealed class EncryptionOptions
{
    public const string SectionName = "Security:Encryption";
    public const int KeySizeBytes = 32;

    /// <summary>Id of the key-encryption key (KEK) used to wrap newly created data keys.</summary>
    public string CurrentKeyEncryptionKeyId { get; set; } = string.Empty;

    /// <summary>KEK id → base64 256-bit key. Retired KEKs stay listed until every data key is re-wrapped.</summary>
    public Dictionary<string, string> KeyEncryptionKeys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Base64 256-bit HMAC key for blind indexes. Must differ from every KEK.</summary>
    public string BlindIndexKey { get; set; } = string.Empty;

    /// <summary>How long a node trusts its cached key ring before reloading it (picks up rotations by other nodes).</summary>
    public int KeyRingRefreshMinutes { get; set; } = 10;

    /// <summary>Set only when Development started without configured keys and random ones were generated.</summary>
    public bool UsesEphemeralDevelopmentKeys { get; set; }
}
