using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Bookings.NoShow;

public sealed record MarkNoShowCommand(
    Guid VenueId,
    Guid BookingId,
    string? Reason,
    Guid ActorUserId
) : ICommand<Result>;

public sealed class MarkNoShowHandler : ICommandHandler<MarkNoShowCommand, Result>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingRealtimeBroadcaster _broadcaster;

    public MarkNoShowHandler(IBookingDbContext dbContext, IClock clock, IBookingRealtimeBroadcaster broadcaster)
    {
        _dbContext = dbContext;
        _clock = clock;
        _broadcaster = broadcaster;
    }

    public async Task<Result> HandleAsync(MarkNoShowCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var booking = await _dbContext.Bookings
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == command.BookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        if (booking.VenueId != command.VenueId)
        {
            return Result.Failure(Error.Forbidden("Booking.Forbidden", "Booking does not belong to the specified venue."));
        }

        var result = booking.MarkNoShow(command.Reason ?? "No show", command.ActorUserId.ToString("D"), nowUtc);
        if (result.IsFailure)
        {
            return result;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId,
            booking.VenueId,
            booking.Id,
            booking.BookingReference,
            BookingStatus.NoShow.ToString(),
            nowUtc,
            cancellationToken);

        return Result.Success();
    }
}
