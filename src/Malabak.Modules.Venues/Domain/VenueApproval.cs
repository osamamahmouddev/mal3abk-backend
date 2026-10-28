namespace Malabak.Modules.Venues.Domain;

/// <summary>Admin approval lifecycle; independent from the owner's Published flag (rule.md §6.6).</summary>
public enum VenueApprovalStatus
{
    Draft = 0,
    Submitted = 1,
    NeedsInfo = 2,
    Approved = 3,
    Rejected = 4
}

public enum VenueApprovalAction
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2,
    InfoRequested = 3
}

/// <summary>One step of the approval conversation (owner submissions and admin decisions, with their messages).</summary>
public sealed class VenueApprovalEvent
{
    public const int MaxMessageLength = 2000;

    private VenueApprovalEvent()
    {
    }

    public Guid Id { get; private set; }
    public Guid VenueId { get; private set; }
    public VenueApprovalAction Action { get; private set; }
    public VenueApprovalStatus FromStatus { get; private set; }
    public VenueApprovalStatus ToStatus { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string? Message { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    internal static VenueApprovalEvent Create(
        Guid venueId,
        VenueApprovalAction action,
        VenueApprovalStatus from,
        VenueApprovalStatus to,
        Guid actorUserId,
        string? message,
        DateTime nowUtc)
    {
        var trimmed = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (trimmed?.Length > MaxMessageLength)
        {
            throw new ArgumentException("Message is too long.", nameof(message));
        }

        return new VenueApprovalEvent
        {
            Id = Guid.CreateVersion7(),
            VenueId = venueId,
            Action = action,
            FromStatus = from,
            ToStatus = to,
            ActorUserId = actorUserId,
            Message = trimmed,
            CreatedAtUtc = nowUtc
        };
    }
}
