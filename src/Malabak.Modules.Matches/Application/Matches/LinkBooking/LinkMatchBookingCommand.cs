using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.LinkBooking;

public sealed record LinkMatchBookingCommand(
    Guid MatchId,
    Guid CallerTeamId,
    Guid BookingId,
    Guid CallerPlayerId) : ICommand<Result<MatchDto>>;
