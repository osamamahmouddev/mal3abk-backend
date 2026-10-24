using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Application.Bookings.CreateBooking;

public sealed record CreateBookingCommand(
    Guid PitchId,
    Guid PlayerId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal TotalPrice,
    decimal DepositAmount,
    string ActorUserId
) : ICommand<Result<CreateBookingResponse>>;
