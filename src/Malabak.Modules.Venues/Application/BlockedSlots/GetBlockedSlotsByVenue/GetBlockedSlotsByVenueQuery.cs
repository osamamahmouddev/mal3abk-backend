using Malabak.Modules.Venues.Application.BlockedSlots;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.BlockedSlots.GetBlockedSlotsByVenue;

public sealed record GetBlockedSlotsByVenueQuery(Guid VenueId, Guid UserId) : IQuery<Result<IReadOnlyList<BlockedSlotDto>>>;
