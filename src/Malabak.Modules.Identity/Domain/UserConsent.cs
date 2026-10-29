namespace Malabak.Modules.Identity.Domain;

public enum ConsentType
{
    Terms = 0,
    Privacy = 1,
    Notifications = 2,
    Location = 3,
    Marketing = 4
}

/// <summary>
/// Append-only record of a consent decision (grant or withdrawal) with the document version shown, so we can prove
/// what the user agreed to and when. The current state is the latest record per type.
/// </summary>
public sealed class UserConsent
{
    public const int MaxVersionLength = 20;

    private UserConsent()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public ConsentType Type { get; private set; }
    public bool Granted { get; private set; }

    /// <summary>Version of the terms/privacy document; null for device permissions.</summary>
    public string? Version { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    public static UserConsent Record(Guid userId, ConsentType type, bool granted, string? version, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        Type = type,
        Granted = granted,
        Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
        RecordedAtUtc = nowUtc
    };
}
