namespace Malabak.Modules.Identity.Domain;

public enum WaitlistAudience
{
    Player = 0,
    Venue = 1
}

/// <summary>
/// Lead captured from the landing page waitlist (TASK-814).
/// Phone numbers are stored encrypted (AES-256-GCM) with an HMAC blind index for deduplication per SECURITY.md.
/// Consent is recorded per Egypt Law 151/2020.
/// </summary>
public sealed class WaitlistEntry
{
    private WaitlistEntry()
    {
    }

    public Guid Id { get; private set; }
    public WaitlistAudience Audience { get; private set; }
    public string? ChosenPlan { get; private set; }
    public byte[] PhoneEncrypted { get; private set; } = [];
    public string PhoneBlindIndex { get; private set; } = string.Empty;
    public DateTime ConsentRecordedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    public static WaitlistEntry Create(
        WaitlistAudience audience,
        string? chosenPlan,
        byte[] phoneEncrypted,
        string phoneBlindIndex,
        string? ipAddress,
        string? userAgent,
        DateTime nowUtc) => new()
        {
            Id = Guid.CreateVersion7(),
            Audience = audience,
            ChosenPlan = string.IsNullOrWhiteSpace(chosenPlan) ? null : chosenPlan.Trim(),
            PhoneEncrypted = phoneEncrypted,
            PhoneBlindIndex = phoneBlindIndex,
            ConsentRecordedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            IpAddress = Truncate(ipAddress, 45),
            UserAgent = Truncate(userAgent, 500)
        };

    public void UpdatePlanAndAudience(WaitlistAudience audience, string? chosenPlan, byte[] phoneEncrypted, DateTime nowUtc)
    {
        Audience = audience;
        if (!string.IsNullOrWhiteSpace(chosenPlan))
        {
            ChosenPlan = chosenPlan.Trim();
        }
        PhoneEncrypted = phoneEncrypted;
        UpdatedAtUtc = nowUtc;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
