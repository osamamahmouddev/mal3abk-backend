using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Application.Bookings.ManualBooking;

/// <summary>What the customer of a manual (phone / walk-in) booking has paid so far.</summary>
public enum ManualPaymentStatus
{
    /// <summary>Paid in full; the booking is confirmed.</summary>
    FullyPaid = 0,

    /// <summary>A deposit was paid and the rest is due at the venue; the booking is confirmed.</summary>
    DepositPaid = 1,

    /// <summary>Nothing paid yet: the slot is held until the deposit arrives or the hold lapses.</summary>
    AwaitingDeposit = 2,

    /// <summary>
    /// Booked but not confirmed yet (no deposit asked): the slot stays taken until the venue confirms or cancels it, and is
    /// released when the match time comes if it was never confirmed.
    /// </summary>
    Unconfirmed = 3
}

public sealed record CreateManualBookingCommand(
    Guid VenueId,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal Price,
    string Currency,
    string? CustomerName,
    string? CustomerPhone,
    string ActorUserId,
    string? Notes = null,
    ManualPaymentStatus PaymentStatus = ManualPaymentStatus.FullyPaid,
    decimal? DepositAmount = null
) : ICommand<Result<ManualBookingResponse>>;

public sealed record ManualBookingResponse(
    Guid BookingId,
    string BookingReference,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal Price,
    string Currency,
    string? CustomerName,
    string Status = "Confirmed",
    decimal DepositAmount = 0,
    DateTime? HoldExpiresAtUtc = null);
