using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Malabak.Modules.Booking.Application.Bookings.ManualBooking;

public sealed class CreateManualBookingHandler : ICommandHandler<CreateManualBookingCommand, Result<ManualBookingResponse>>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingReferenceGenerator _refGenerator;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public CreateManualBookingHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IBookingReferenceGenerator refGenerator,
        IAvailabilityInvalidator availabilityInvalidator,
        IBookingRealtimeBroadcaster broadcaster,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _refGenerator = refGenerator;
        _availabilityInvalidator = availabilityInvalidator;
        _broadcaster = broadcaster;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public async Task<Result<ManualBookingResponse>> HandleAsync(CreateManualBookingCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        if (command.EndUtc <= command.StartUtc)
        {
            return Result.Failure<ManualBookingResponse>(Error.Validation("Booking.InvalidTimeRange", "End time must be after start time."));
        }

        // Generate reference
        var bookingReference = _refGenerator.Generate();

        var bookingId = Guid.NewGuid();
        var price = new Money(command.Price, command.Currency);

        var staffUserId = Guid.TryParse(command.ActorUserId, out var parsedGuid) ? parsedGuid : Guid.NewGuid();

        var bookingResult = Domain.Booking.CreateManual(
            bookingId,
            bookingReference,
            command.VenueId,
            command.PitchId,
            staffUserId,
            command.CustomerName,
            command.CustomerPhone ?? "N/A",
            command.StartUtc,
            command.EndUtc,
            price,
            price,
            confirmedImmediately: true,
            holdExpiresAtUtc: null,
            nowUtc: nowUtc);

        if (bookingResult.IsFailure)
        {
            return Result.Failure<ManualBookingResponse>(bookingResult.Error);
        }

        var booking = bookingResult.Value;
        _dbContext.Bookings.Add(booking);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsExclusionOrUniqueViolation(ex))
        {
            return Result.Failure<ManualBookingResponse>(BookingErrors.SlotOverlap);
        }

        // Invalidate availability
        await _availabilityInvalidator.InvalidatePitchAsync(command.PitchId, cancellationToken);

        // Broadcast slot status change to booked
        await _broadcaster.BroadcastSlotStatusChangedAsync(
            command.VenueId,
            command.PitchId,
            command.StartUtc,
            command.EndUtc,
            "booked",
            cancellationToken);

        await _domainEventDispatcher.DispatchAsync(booking.DomainEvents, cancellationToken);

        var response = new ManualBookingResponse(
            booking.Id,
            booking.BookingReference,
            booking.PitchId,
            booking.StartUtc,
            booking.EndUtc,
            booking.TotalPrice.Amount,
            booking.TotalPrice.Currency,
            command.CustomerName);

        return Result.Success(response);
    }

    private static bool IsExclusionOrUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pgEx)
        {
            return pgEx.SqlState is "23P01" or "23505"
                || pgEx.ConstraintName == IBookingDbContext.ExclusionConstraintName;
        }

        return ex.Message.Contains(IBookingDbContext.ExclusionConstraintName, StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("exclusion", StringComparison.OrdinalIgnoreCase);
    }
}
