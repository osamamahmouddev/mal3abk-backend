namespace Malabak.Modules.Identity.Domain;

public enum SmsDeliveryStatus
{
    Queued = 0,
    Sent = 1,
    Delivered = 2,
    Failed = 3,
    Blocked = 4
}

/// <summary>
/// Record of one outgoing message for spend caps and delivery tracking. Never stores the body or the full
/// number: only a masked number and a hash (enough to correlate abuse without keeping PII around).
/// </summary>
public sealed class SmsDispatch
{
    private SmsDispatch()
    {
    }

    public Guid Id { get; private set; }
    public string MaskedPhone { get; private set; } = string.Empty;
    public string PhoneHash { get; private set; } = string.Empty;
    public string Template { get; private set; } = string.Empty;
    public string Channel { get; private set; } = string.Empty;
    public string? Provider { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public SmsDeliveryStatus Status { get; private set; }
    public decimal CostEgp { get; private set; }
    public string? Error { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static SmsDispatch Create(string maskedPhone, string phoneHash, string template, string channel, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        MaskedPhone = maskedPhone,
        PhoneHash = phoneHash,
        Template = template,
        Channel = channel,
        Status = SmsDeliveryStatus.Queued,
        CreatedAtUtc = nowUtc
    };

    public void MarkSent(string provider, string? providerMessageId, decimal costEgp, DateTime nowUtc)
    {
        Provider = provider;
        ProviderMessageId = providerMessageId;
        CostEgp = costEgp;
        Status = SmsDeliveryStatus.Sent;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkFailed(SmsDeliveryStatus status, string error, DateTime nowUtc)
    {
        Status = status;
        Error = error.Length <= 300 ? error : error[..300];
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Delivery receipts can arrive out of order; never move a delivered message back to sent.</summary>
    public void ApplyDeliveryStatus(SmsDeliveryStatus status, DateTime nowUtc)
    {
        if (Status == SmsDeliveryStatus.Delivered && status == SmsDeliveryStatus.Sent)
        {
            return;
        }

        Status = status;
        UpdatedAtUtc = nowUtc;
    }
}
