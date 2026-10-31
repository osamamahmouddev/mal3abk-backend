using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Application.Bookings.ManualBooking;

public sealed record CreateManualBookingCommand(
    Guid VenueId,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal Price,
    string Currency,
    string CustomerName,
    string? CustomerPhone,
    string ActorUserId,
    string? Notes = null
) : ICommand<Result<ManualBookingResponse>>;

public sealed record ManualBookingResponse(
    Guid BookingId,
    string BookingReference,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal Price,
    string Currency,
    string CustomerName);
