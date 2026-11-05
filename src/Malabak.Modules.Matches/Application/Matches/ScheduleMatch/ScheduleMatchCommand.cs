using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.ScheduleMatch;

public sealed record ScheduleMatchCommand(
    Guid HomeTeamId,
    Guid AwayTeamId,
    DateTime ScheduledStartUtc,
    int DurationMinutes,
    Guid? PitchId,
    Guid? VenueId,
    string? VenueName,
    Guid? BookingId,
    string? Notes,
    Guid CallerPlayerId) : ICommand<Result<MatchDto>>;
