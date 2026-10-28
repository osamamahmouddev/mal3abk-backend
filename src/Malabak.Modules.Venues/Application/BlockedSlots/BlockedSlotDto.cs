namespace Malabak.Modules.Venues.Application.BlockedSlots;

public sealed record BlockedSlotDto(
    Guid Id,
    Guid VenueId,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    string Reason,
    Guid BlockedByUserId,
    DateTime CreatedAtUtc);
