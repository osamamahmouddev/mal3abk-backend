using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.BlockedSlots.DeleteBlockedSlot;

public sealed record DeleteBlockedSlotCommand(Guid VenueId, Guid BlockedSlotId, Guid UserId) : ICommand<Result>;
