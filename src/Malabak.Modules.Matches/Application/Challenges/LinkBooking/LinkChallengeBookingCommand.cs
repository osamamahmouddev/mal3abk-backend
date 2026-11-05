using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Challenges.LinkBooking;

public sealed record LinkChallengeBookingCommand(
    Guid ChallengeId,
    Guid TeamId,
    Guid CallerPlayerId,
    Guid BookingId) : ICommand<Result<TeamChallengeDto>>;
