using Malabak.Modules.Venues.Application.BlockedSlots;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.BlockedSlots.CreateBlockedSlot;

public sealed record CreateBlockedSlotCommand(
    Guid VenueId,
    Guid PitchId,
    Guid UserId,
    DateTime StartUtc,
    DateTime EndUtc,
    string Reason) : ICommand<Result<BlockedSlotDto>>;
