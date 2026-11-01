using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Malabak.Modules.Booking.Application.Bookings.CreateBooking;

public sealed class CreateBookingHandler : ICommandHandler<CreateBookingCommand, Result<CreateBookingResponse>>
{
    private const int _maxPendingHoldsPerPlayer = 2;
    private const int _maxReferenceGenerationRetries = 5;

    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingQuoteValidator _quoteValidator;
    private readonly IBookingPolicyProvider _policyProvider;
    private readonly IBookingReferenceGenerator _refGenerator;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly IHoldExpiryScheduler _holdExpiryScheduler;
    private readonly IBookingRealtimeBroadcaster _broadcaster;

    public CreateBookingHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IBookingQuoteValidator quoteValidator,
        IBookingPolicyProvider policyProvider,
        IBookingReferenceGenerator refGenerator,
        IAvailabilityInvalidator availabilityInvalidator,
        IDomainEventDispatcher domainEventDispatcher,
        IHoldExpiryScheduler holdExpiryScheduler,
        IBookingRealtimeBroadcaster broadcaster)
    {
        _dbContext = dbContext;
        _clock = clock;
        _quoteValidator = quoteValidator;
        _policyProvider = policyProvider;
        _refGenerator = refGenerator;
        _availabilityInvalidator = availabilityInvalidator;
        _domainEventDispatcher = domainEventDispatcher;
        _holdExpiryScheduler = holdExpiryScheduler;
        _broadcaster = broadcaster;
    }

    public async Task<Result<CreateBookingResponse>> HandleAsync(CreateBookingCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        // 1. Quota check: Player cannot exceed max pending holds (slot hoarding prevention)
        var pendingHoldsCount = await _dbContext.Bookings.AsNoTracking()
            .CountAsync(b => b.PlayerId == command.PlayerId && b.Status == BookingStatus.PendingDeposit && !b.IsDeleted, cancellationToken);

        if (pendingHoldsCount >= _maxPendingHoldsPerPlayer)
        {
            return Result.Failure<CreateBookingResponse>(BookingErrors.MaxPendingBookingsExceeded);
        }

        // 2. Validate price quote token & slot availability
        var quoteResult = await _quoteValidator.ValidateAsync(
            command.QuoteToken,
            command.PlayerId,
            command.PitchId,
            command.StartUtc,
            command.EndUtc,
            cancellationToken);

        if (quoteResult.IsFailure)
        {
            return Result.Failure<CreateBookingResponse>(quoteResult.Error);
        }

        var priceQuote = quoteResult.Value;

        // 3. Verify client expectedPrice matches server-computed total
        if (command.ExpectedPrice != priceQuote.Total)
        {
            return Result.Failure<CreateBookingResponse>(Error.Conflict("Booking.PriceMismatch", "The slot price has changed since the quote was requested."));
        }

        // 4. Fetch venue booking policy snapshot
        var policySnapshot = await _policyProvider.GetCurrentSnapshotAsync(priceQuote.VenueId, cancellationToken);
        var holdDurationMinutes = policySnapshot?.ProofSubmissionWindowMinutes is >= 10 and <= 1440
            ? policySnapshot.ProofSubmissionWindowMinutes
            : 15;
        var holdExpiresAtUtc = nowUtc.AddMinutes(holdDurationMinutes);

        // 5. Generate collision-safe reference
        string bookingReference = string.Empty;
        for (var attempt = 0; attempt < _maxReferenceGenerationRetries; attempt++)
        {
            var candidate = _refGenerator.Generate();
            var exists = await _dbContext.Bookings.AsNoTracking()
                .AnyAsync(b => b.BookingReference == candidate, cancellationToken);
            if (!exists)
            {
                bookingReference = candidate;
                break;
            }
        }

        if (string.IsNullOrEmpty(bookingReference))
        {
            bookingReference = $"MLB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        }

        // 6. Create Booking aggregate
        var bookingId = Guid.NewGuid();
        var bookingResult = Domain.Booking.Create(
            bookingId,
            bookingReference,
            priceQuote.VenueId,
            command.PitchId,
            command.PlayerId,
            command.StartUtc,
            command.EndUtc,
            new Money(priceQuote.Total, priceQuote.Currency),
            new Money(priceQuote.Deposit, priceQuote.Currency),
            holdExpiresAtUtc,
            priceQuote,
            policySnapshot,
            nowUtc,
            command.ActorUserId);

        if (bookingResult.IsFailure)
        {
            return Result.Failure<CreateBookingResponse>(bookingResult.Error);
        }

        var booking = bookingResult.Value;
        _dbContext.Bookings.Add(booking);

        // 7. Save changes with PostgreSQL GiST exclusion constraint protection against double bookings
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsExclusionOrUniqueViolation(ex))
        {
            return Result.Failure<CreateBookingResponse>(BookingErrors.SlotOverlap);
        }

        // 8. Invalidate availability caches
        await _availabilityInvalidator.InvalidatePitchAsync(command.PitchId, cancellationToken);

        // 9. Dispatch domain events
        await _domainEventDispatcher.DispatchAsync(booking.DomainEvents, cancellationToken);

        // 10. Schedule hold expiry
        _holdExpiryScheduler.ScheduleHoldExpiry(booking.Id, holdExpiresAtUtc);

        // 11. Broadcast realtime slot hold and booking updated
        await _broadcaster.BroadcastSlotStatusChangedAsync(
            booking.VenueId,
            booking.PitchId,
            booking.StartUtc,
            booking.EndUtc,
            "held",
            cancellationToken);

        await _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId,
            booking.VenueId,
            booking.Id,
            booking.BookingReference,
            booking.Status.ToString(),
            nowUtc,
            cancellationToken);

        var paymentInstructions = policySnapshot is null
            ? "Please transfer the deposit amount and upload your payment receipt within the hold window."
            : $"Please transfer the deposit amount of {priceQuote.Deposit} {priceQuote.Currency} within {holdDurationMinutes} minutes and upload your proof receipt.";

        var response = new CreateBookingResponse(
            booking.Id,
            booking.BookingReference,
            booking.VenueId,
            booking.PitchId,
            booking.StartUtc,
            booking.EndUtc,
            booking.TotalPrice.Amount,
            booking.DepositAmount.Amount,
            booking.TotalPrice.Currency,
            booking.Status,
            holdExpiresAtUtc,
            paymentInstructions);

        return Result.Success(response);
    }

    private static bool IsExclusionOrUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pgEx)
        {
            // 23P01 = exclusion_violation, 23505 = unique_violation
            return pgEx.SqlState is "23P01" or "23505"
                || pgEx.ConstraintName == IBookingDbContext.ExclusionConstraintName;
        }

        return ex.Message.Contains(IBookingDbContext.ExclusionConstraintName, StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("exclusion", StringComparison.OrdinalIgnoreCase);
    }
}
