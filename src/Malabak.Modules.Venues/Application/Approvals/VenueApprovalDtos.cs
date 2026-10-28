using Malabak.Modules.Venues.Domain;

namespace Malabak.Modules.Venues.Application.Approvals;

public sealed record VenueApprovalEventDto(
    string Action,
    string FromStatus,
    string ToStatus,
    Guid ActorUserId,
    string? Message,
    DateTime CreatedAtUtc);

public sealed record VenueApprovalDto(
    Guid VenueId,
    string ApprovalStatus,
    bool IsPublished,
    bool IsSearchable,
    IReadOnlyList<VenueApprovalEventDto> History);

public sealed record PendingVenueApprovalDto(
    Guid VenueId,
    string Name,
    string City,
    string Area,
    Guid OwnerId,
    DateTime? SubmittedAtUtc);

public enum VenueApprovalDecision
{
    Approve = 0,
    Reject = 1,
    RequestInfo = 2
}

public static class VenueApprovalMapping
{
    public static VenueApprovalEventDto ToDto(VenueApprovalEvent e) =>
        new(e.Action.ToString(), e.FromStatus.ToString(), e.ToStatus.ToString(), e.ActorUserId, e.Message, e.CreatedAtUtc);
}
