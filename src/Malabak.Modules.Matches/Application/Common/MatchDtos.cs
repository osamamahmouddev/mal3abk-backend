namespace Malabak.Modules.Matches.Application.Common;

public sealed record PagedList<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record MatchDto(
    Guid Id,
    Guid? ChallengeId,
    Guid HomeTeamId,
    string HomeTeamName,
    Guid AwayTeamId,
    string AwayTeamName,
    Guid? PitchId,
    Guid? VenueId,
    string? VenueName,
    DateTime ScheduledStartUtc,
    DateTime ScheduledEndUtc,
    int DurationMinutes,
    Guid? BookingId,
    string Status,
    string Outcome,
    int? HomeScore,
    int? AwayScore,
    string? BookingDeepLink,
    string? Notes,
    DateTime CreatedAtUtc);

public sealed record ScheduleMatchRequest(
    Guid AwayTeamId,
    DateTime ScheduledStartUtc,
    int DurationMinutes = 60,
    Guid? PitchId = null,
    Guid? VenueId = null,
    string? VenueName = null,
    Guid? BookingId = null,
    string? Notes = null);

public sealed record CancelMatchRequest(
    string? Reason);

public sealed record LinkMatchBookingRequest(
    Guid BookingId);
