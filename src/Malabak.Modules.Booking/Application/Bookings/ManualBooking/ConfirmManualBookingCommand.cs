using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Application.Bookings.ManualBooking;

public sealed record ConfirmManualBookingCommand(
    Guid VenueId,
    Guid BookingId,
    Guid ActorUserId,
    decimal? CollectedDeposit = null,
    string? Notes = null
) : ICommand<Result>;
