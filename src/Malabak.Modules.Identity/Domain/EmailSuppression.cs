namespace Malabak.Modules.Identity.Domain;

public enum EmailSuppressionReason
{
    HardBounce = 0,
    Complaint = 1
}

/// <summary>
/// Address we must stop emailing (hard bounce or spam complaint) to protect the sending domain's reputation.
/// Stored as a SHA-256 of the normalized address, not the address itself.
/// </summary>
public sealed class EmailSuppression
{
    private EmailSuppression()
    {
    }

    public string EmailHash { get; private set; } = string.Empty;
    public EmailSuppressionReason Reason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static EmailSuppression Create(string emailHash, EmailSuppressionReason reason, DateTime nowUtc) =>
        new() { EmailHash = emailHash, Reason = reason, CreatedAtUtc = nowUtc };
}
