using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Application.Bookings.CreateBooking;

public sealed record CreateBookingCommand(
    Guid PitchId,
    Guid PlayerId,
    DateTime StartUtc,
    DateTime EndUtc,
    string QuoteToken,
    decimal ExpectedPrice,
    string ActorUserId,
    string? CustomerName = null,
    string? CustomerPhone = null
) : ICommand<Result<CreateBookingResponse>>;
