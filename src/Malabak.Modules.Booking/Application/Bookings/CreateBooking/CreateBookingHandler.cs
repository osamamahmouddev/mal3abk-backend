using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Application.Bookings.CreateBooking;

public sealed class CreateBookingHandler : ICommandHandler<CreateBookingCommand, Result<CreateBookingResponse>>
{
    private readonly IClock _clock;

    public CreateBookingHandler(IClock clock)
    {
        _clock = clock;
    }

    public Task<Result<CreateBookingResponse>> HandleAsync(CreateBookingCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var holdExpiresAtUtc = nowUtc.AddMinutes(15);
        var bookingId = Guid.NewGuid();
        var shortRef = $"MLB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var bookingResult = Domain.Booking.Create(
            bookingId,
            shortRef,
            command.PitchId,
            command.PlayerId,
            command.StartUtc,
            command.EndUtc,
            new Money(command.TotalPrice),
            new Money(command.DepositAmount),
            holdExpiresAtUtc,
            nowUtc,
            command.ActorUserId
        );

        if (bookingResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<CreateBookingResponse>(bookingResult.Error));
        }

        var response = new CreateBookingResponse(
            bookingResult.Value.Id,
            bookingResult.Value.BookingReference,
            bookingResult.Value.Status,
            holdExpiresAtUtc
        );

        return Task.FromResult(Result.Success(response));
    }
}
