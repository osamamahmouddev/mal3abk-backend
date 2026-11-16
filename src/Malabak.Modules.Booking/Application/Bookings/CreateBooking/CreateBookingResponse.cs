using Malabak.Modules.Booking.Domain;

namespace Malabak.Modules.Booking.Application.Bookings.CreateBooking;

public sealed record CreateBookingResponse(
    Guid BookingId,
    string BookingReference,
    Guid VenueId,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal TotalPrice,
    decimal DepositAmount,
    string Currency,
    BookingStatus Status,
    DateTime HoldExpiresAtUtc,
    string? PaymentInstructions,
    decimal PlatformCommission = 0m,
    decimal VenueNetAmount = 0m,
    decimal OnlineDepositAmount = 0m,
    decimal VenuePayAmount = 0m
);
