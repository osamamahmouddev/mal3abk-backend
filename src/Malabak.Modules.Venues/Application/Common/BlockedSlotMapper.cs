using Malabak.Modules.Venues.Application.BlockedSlots;
using Malabak.Modules.Venues.Domain;

namespace Malabak.Modules.Venues.Application.Common;

public static class BlockedSlotMapper
{
    public static BlockedSlotDto ToDto(BlockedSlot slot)
    {
        return new BlockedSlotDto(
            slot.Id,
            slot.VenueId,
            slot.PitchId,
            slot.StartUtc,
            slot.EndUtc,
            slot.Reason,
            slot.BlockedByUserId,
            slot.CreatedAtUtc);
    }
}
