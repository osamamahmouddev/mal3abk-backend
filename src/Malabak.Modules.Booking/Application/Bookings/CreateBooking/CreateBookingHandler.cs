using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.CreateBooking;

/// <summary>
/// Creates a booking in <see cref="BookingStatus.PendingDeposit"/> and holds the slot (TASK-032). Nothing the client says
/// about price or availability is trusted: the quote token is re-validated and the slot re-priced by Venues; the
/// per-player pending limit is enforced under a per-player lock; writers to one pitch are serialised; and the database's
/// exclusion constraint has the last word on overlaps, whichever request wins a race.
/// </summary>
public sealed class CreateBookingHandler : ICommandHandler<CreateBookingCommand, Result<CreateBookingResponse>>
{
    private const int _maxPendingHoldsPerPlayer = BookingHoldRules.MaxPendingBookingsPerPlayer;
    private const int _maxReferenceAttempts = 5;

    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingQuoteValidator _quoteValidator;
    private readonly IBookingPolicyProvider _policyProvider;
    private readonly IBookingReferenceGenerator _refGenerator;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly IHoldExpiryScheduler _holdExpiryScheduler;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly ILogger<CreateBookingHandler> _logger;

    public CreateBookingHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IBookingQuoteValidator quoteValidator,
        IBookingPolicyProvider policyProvider,
        IBookingReferenceGenerator refGenerator,
        IAvailabilityInvalidator availabilityInvalidator,
        IHoldExpiryScheduler holdExpiryScheduler,
        IBookingRealtimeBroadcaster broadcaster,
        ILogger<CreateBookingHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _quoteValidator = quoteValidator;
        _policyProvider = policyProvider;
        _refGenerator = refGenerator;
        _availabilityInvalidator = availabilityInvalidator;
        _holdExpiryScheduler = holdExpiryScheduler;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task<Result<CreateBookingResponse>> HandleAsync(CreateBookingCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        // 1. Validate the price quote token and the slot (hours, notice window, availability), and re-price it.
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

        // 2. The price the client expects must match what the server computes.
        if (command.ExpectedPrice != priceQuote.Total)
        {
            return Result.Failure<CreateBookingResponse>(Error.Conflict("Booking.PriceMismatch", "The slot price has changed since the quote was requested."));
        }

        // 3. The hold lasts as long as the venue's policy gives the player to send the deposit proof.
        var policySnapshot = await _policyProvider.GetCurrentSnapshotAsync(priceQuote.VenueId, cancellationToken);
        var holdDurationMinutes = policySnapshot?.ProofSubmissionWindowMinutes is >= 10 and <= 1440
            ? policySnapshot.ProofSubmissionWindowMinutes
            : 15;
        var holdExpiresAtUtc = nowUtc.AddMinutes(holdDurationMinutes);

        // 4. Quota, lapsed holds and the insert, in one transaction.
        var created = await CreateWithUniqueReferenceAsync(command, priceQuote, policySnapshot, holdExpiresAtUtc, nowUtc, cancellationToken);
        if (created.IsFailure)
        {
            return Result.Failure<CreateBookingResponse>(created.Error);
        }

        var booking = created.Value;

        // 5. The booking is committed. Nothing below may turn that into a failure for the caller.
        await AfterCommitAsync(booking, holdExpiresAtUtc, nowUtc, cancellationToken);

        var paymentInstructions = policySnapshot is null
            ? "Please transfer the deposit amount and upload your payment receipt within the hold window."
            : $"Please transfer the deposit amount of {priceQuote.Deposit} {priceQuote.Currency} within {holdDurationMinutes} minutes and upload your proof receipt.";

        return Result.Success(new CreateBookingResponse(
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
            paymentInstructions,
            booking.PlatformCommission.Amount,
            booking.VenueNetAmount.Amount,
            booking.PlatformCommission.Amount,
            booking.VenuePayAmount.Amount));
    }

    /// <summary>
    /// A random reference can, rarely, collide with an existing one. The unique index is the guard, so a collision is
    /// retried with a fresh reference; an overlap is not retried because no reference can fix it.
    /// </summary>
    private async Task<Result<Domain.Booking>> CreateWithUniqueReferenceAsync(
        CreateBookingCommand command,
        PriceQuoteSnapshot priceQuote,
        BookingPolicySnapshot? policySnapshot,
        DateTime holdExpiresAtUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _maxReferenceAttempts; attempt++)
        {
            var (outcome, collided) = await TryCreateAsync(command, priceQuote, policySnapshot, holdExpiresAtUtc, nowUtc, cancellationToken);
            if (!collided)
            {
                return outcome!;
            }

            _logger.LogWarning("Booking reference collided on attempt {Attempt}; retrying with a new one.", attempt);
        }

        _logger.LogError("Could not find an unused booking reference in {Attempts} attempts.", _maxReferenceAttempts);
        return Result.Failure<Domain.Booking>(BookingErrors.ReferenceUnavailable);
    }

    private async Task<(Result<Domain.Booking>? Outcome, bool Collided)> TryCreateAsync(
        CreateBookingCommand command,
        PriceQuoteSnapshot priceQuote,
        BookingPolicySnapshot? policySnapshot,
        DateTime holdExpiresAtUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Always the player lock first, then the pitch lock, so two requests can never wait on each other.
        await BookingConcurrency.LockPlayerAsync(_dbContext, command.PlayerId, cancellationToken);
        await BookingConcurrency.LockPitchAsync(_dbContext, command.PitchId, cancellationToken);

        await BookingConcurrency.ReleaseLapsedHoldsAsync(_dbContext, command.PlayerId, command.PitchId, command.StartUtc, command.EndUtc, nowUtc, cancellationToken);

        // Penalty: three unpaid holds lapsing within 24 hours lock the player out for six hours. Server clock only.
        var lapsedSince = nowUtc - BookingHoldRules.PenaltyWindow;
        var recentLapses = await _dbContext.Bookings
            .Where(b => b.PlayerId == command.PlayerId && b.Status == BookingStatus.Expired && !b.IsDeleted
                && b.HoldExpiresAtUtc > lapsedSince && b.HoldExpiresAtUtc <= nowUtc
                && !_dbContext.PaymentProofs.Any(p => p.BookingId == b.Id))
            .OrderByDescending(b => b.HoldExpiresAtUtc)
            .Select(b => b.HoldExpiresAtUtc!.Value)
            .Take(BookingHoldRules.ExpiredHoldsForPenalty)
            .ToListAsync(cancellationToken);
        if (recentLapses.Count == BookingHoldRules.ExpiredHoldsForPenalty && nowUtc < recentLapses[0] + BookingHoldRules.PenaltyLockDuration)
        {
            return (Result.Failure<Domain.Booking>(BookingErrors.AccountLockedForHoarding), false);
        }

        // Slot hoarding prevention: only holds that are still running count.
        var pendingHolds = await _dbContext.Bookings.CountAsync(
            b => b.PlayerId == command.PlayerId && b.Status == BookingStatus.PendingDeposit && b.HoldExpiresAtUtc > nowUtc && !b.IsDeleted,
            cancellationToken);
        if (pendingHolds >= _maxPendingHoldsPerPlayer)
        {
            return (Result.Failure<Domain.Booking>(BookingErrors.MaxPendingBookingsExceeded), false);
        }

        var bookingResult = Domain.Booking.Create(
            IdGenerator.NewGuid(),
            _refGenerator.Generate(),
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
            return (Result.Failure<Domain.Booking>(bookingResult.Error), false);
        }

        _dbContext.Bookings.Add(bookingResult.Value);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (Result.Success(bookingResult.Value), false);
        }
        catch (DbUpdateException ex) when (BookingPersistenceErrors.IsSlotOverlap(ex))
        {
            return (Result.Failure<Domain.Booking>(BookingErrors.SlotOverlap), false);
        }
        catch (DbUpdateException ex) when (BookingPersistenceErrors.IsReferenceCollision(ex))
        {
            return (null, true);
        }
    }

    /// <summary>
    /// Cached availability, domain events, the expiry job and live updates. The booking already exists, so a Redis,
    /// Hangfire or SignalR outage must not return an error that makes the player retry into a conflict with their own
    /// booking; the periodic expiry sweeper and the cache's own expiry cover what a failed step skipped.
    /// </summary>
    private async Task AfterCommitAsync(Domain.Booking booking, DateTime holdExpiresAtUtc, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await RunGuardedAsync(booking, "clear cached availability", () => _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken));
        await RunGuardedAsync(booking, "schedule the hold expiry", () =>
        {
            _holdExpiryScheduler.ScheduleHoldExpiry(booking.Id, holdExpiresAtUtc);
            return Task.CompletedTask;
        });
        await RunGuardedAsync(booking, "broadcast the held slot", () => _broadcaster.BroadcastSlotStatusChangedAsync(
            booking.VenueId, booking.PitchId, booking.StartUtc, booking.EndUtc, "held", cancellationToken));
        await RunGuardedAsync(booking, "broadcast the booking update", () => _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference, booking.Status.ToString(), nowUtc, cancellationToken));
    }

    private async Task RunGuardedAsync(Domain.Booking booking, string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Booking {BookingId} was created but the step '{Step}' failed.", booking.Id, step);
        }
    }
}
