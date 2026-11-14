using Malabak.SharedKernel;

namespace Malabak.Modules.Subscriptions.Domain;

public enum SubscriptionRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// A venue owner's request to upgrade or change their subscription plan.
/// Reviewed and approved/rejected or replied to by administrators from the billing dashboard.
/// </summary>
public sealed class SubscriptionChangeRequest
{
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid PlanId { get; private set; }
    public BillingCycle Cycle { get; private set; }
    public SubscriptionRequestStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public string? AdminReply { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedBy { get; private set; }

    private SubscriptionChangeRequest() { }

    public static SubscriptionChangeRequest Create(Guid ownerId, Guid planId, BillingCycle cycle, string? notes, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            PlanId = planId,
            Cycle = cycle,
            Status = SubscriptionRequestStatus.Pending,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAtUtc = now
        };

    public void UpdatePending(Guid planId, BillingCycle cycle, string? notes, DateTime now)
    {
        PlanId = planId;
        Cycle = cycle;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedAtUtc = now;
        AdminReply = null;
        ReviewedAtUtc = null;
        ReviewedBy = null;
    }

    public void Approve(Guid adminId, DateTime now)
    {
        Status = SubscriptionRequestStatus.Approved;
        ReviewedBy = adminId;
        ReviewedAtUtc = now;
    }

    public void Reject(Guid adminId, string? reason, DateTime now)
    {
        Status = SubscriptionRequestStatus.Rejected;
        ReviewedBy = adminId;
        ReviewedAtUtc = now;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            AdminReply = reason.Trim();
        }
    }

    public void Reply(Guid adminId, string replyMessage, DateTime now)
    {
        AdminReply = replyMessage.Trim();
        ReviewedBy = adminId;
        ReviewedAtUtc = now;
    }
}
