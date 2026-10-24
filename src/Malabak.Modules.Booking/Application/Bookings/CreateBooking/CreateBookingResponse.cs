using Malabak.Modules.Booking.Domain;

namespace Malabak.Modules.Booking.Application.Bookings.CreateBooking;

public sealed record CreateBookingResponse(
    Guid BookingId,
    string BookingReference,
    BookingStatus Status,
    DateTime HoldExpiresAtUtc
);
