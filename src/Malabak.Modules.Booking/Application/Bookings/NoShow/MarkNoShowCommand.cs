using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.NoShow;

public sealed record MarkNoShowCommand(
    Guid VenueId,
    Guid BookingId,
    string? Reason,
    Guid ActorUserId
) : ICommand<Result>;

/// <summary>
/// The venue marks a confirmed booking as a no-show once its start time has passed (TASK-405). Saved first, announced after.
/// </summary>
public sealed class MarkNoShowHandler : ICommandHandler<MarkNoShowCommand, Result>
{
    public const int MaxReasonLength = 500;

    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly ILogger<MarkNoShowHandler> _logger;

    public MarkNoShowHandler(IBookingDbContext dbContext, IClock clock, IBookingRealtimeBroadcaster broadcaster, ILogger<MarkNoShowHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(MarkNoShowCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var booking = await _dbContext.Bookings
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == command.BookingId && !b.IsDeleted, cancellationToken);

        // Another venue's booking looks like one that does not exist, so ids cannot be probed.
        if (booking is null || booking.VenueId != command.VenueId)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        if (command.Reason is { Length: > MaxReasonLength })
        {
            return Result.Failure(Error.Validation("Booking.NoShowReasonTooLong", $"The reason cannot exceed {MaxReasonLength} characters."));
        }

        var result = booking.MarkNoShow(string.IsNullOrWhiteSpace(command.Reason) ? "No show" : command.Reason, command.ActorUserId.ToString("D"), nowUtc);
        if (result.IsFailure)
        {
            return result;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Error.Conflict("Booking.ConcurrentUpdate", "The booking was changed by another request. Please reload and try again."));
        }

        try
        {
            await _broadcaster.BroadcastBookingUpdatedAsync(
                booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference, BookingStatus.NoShow.ToString(), nowUtc, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Booking {BookingId} was marked as a no-show but the live update failed", booking.Id);
        }

        return Result.Success();
    }
}
