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

public sealed record PendingApprovalPitchDto(
    Guid Id,
    string Name,
    string TurfType,
    int Capacity,
    decimal? LengthMeters,
    decimal? WidthMeters,
    IReadOnlyList<string> Photos);

public sealed record PendingVenueApprovalDto(
    Guid VenueId,
    string Name,
    string City,
    string Area,
    Guid OwnerId,
    DateTime? SubmittedAtUtc,
    string Address = "",
    string PhoneNumber = "",
    string? Description = null,
    IReadOnlyList<string>? Facilities = null,
    IReadOnlyList<string>? Photos = null,
    int PitchCount = 0,
    string? OwnerName = null,
    string? SubmissionMessage = null,
    IReadOnlyList<PendingApprovalPitchDto>? Pitches = null);

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
