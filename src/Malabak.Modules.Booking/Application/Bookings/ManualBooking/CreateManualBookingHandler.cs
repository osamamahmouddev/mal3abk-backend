using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.ManualBooking;

/// <summary>
/// A venue books a slot for a walk-in or phone customer (TASK-037). It goes through the same conflict protection as a
/// player booking: writers to one pitch are serialised, lapsed holds on the slot are released, and the database's
/// exclusion constraint decides any remaining race.
/// </summary>
public sealed class CreateManualBookingHandler : ICommandHandler<CreateManualBookingCommand, Result<ManualBookingResponse>>
{
    private const int _maxReferenceAttempts = 5;

    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingReferenceGenerator _refGenerator;
    private readonly IAvailabilityInvalidator _availabilityInvalidator;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly IVenueDirectory _venues;
    private readonly ILogger<CreateManualBookingHandler> _logger;
    private readonly IBookingPolicyProvider? _policyProvider;

    public CreateManualBookingHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IBookingReferenceGenerator refGenerator,
        IAvailabilityInvalidator availabilityInvalidator,
        IBookingRealtimeBroadcaster broadcaster,
        IVenueDirectory venues,
        ILogger<CreateManualBookingHandler> logger,
        IBookingPolicyProvider? policyProvider = null)
    {
        _policyProvider = policyProvider;
        _dbContext = dbContext;
        _clock = clock;
        _refGenerator = refGenerator;
        _availabilityInvalidator = availabilityInvalidator;
        _broadcaster = broadcaster;
        _venues = venues;
        _logger = logger;
    }

    public async Task<Result<ManualBookingResponse>> HandleAsync(CreateManualBookingCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        if (command.EndUtc <= command.StartUtc)
        {
            return Result.Failure<ManualBookingResponse>(Error.Validation("Booking.InvalidTimeRange", "End time must be after start time."));
        }

        // The audit trail must name a real person: an actor id that is not a user id is refused, never replaced.
        if (!Guid.TryParse(command.ActorUserId, out var staffUserId))
        {
            return Result.Failure<ManualBookingResponse>(BookingErrors.UnauthorizedAccess);
        }

        // The venue in the URL was authorised for this user; the pitch id came from the body. A pitch of another venue looks like
        // one that does not exist, so staff of one venue can neither book nor probe another venue's pitches.
        if (await _venues.GetVenueIdOfPitchAsync(command.PitchId, cancellationToken) != command.VenueId)
        {
            return Result.Failure<ManualBookingResponse>(BookingErrors.PitchNotFound);
        }

        var payment = ResolvePayment(command);
        if (payment.IsFailure)
        {
            return Result.Failure<ManualBookingResponse>(payment.Error);
        }

        var price = new Money(command.Price, command.Currency);

        // An unpaid booking holds the slot for as long as the venue gives a player to send the deposit.
        DateTime? holdExpiresAtUtc = null;
        if (!payment.Value.Confirmed)
        {
            var policy = _policyProvider is null ? null : await _policyProvider.GetCurrentSnapshotAsync(command.VenueId, cancellationToken);
            var holdMinutes = policy?.ProofSubmissionWindowMinutes is >= 10 and <= 1440 ? policy.ProofSubmissionWindowMinutes : 60;
            holdExpiresAtUtc = nowUtc.AddMinutes(holdMinutes);
        }

        for (var attempt = 1; attempt <= _maxReferenceAttempts; attempt++)
        {
            var (outcome, collided) = await TryCreateAsync(command, staffUserId, price, payment.Value, holdExpiresAtUtc, nowUtc, cancellationToken);
            if (collided)
            {
                _logger.LogWarning("Booking reference collided on attempt {Attempt}; retrying with a new one.", attempt);
                continue;
            }

            if (outcome!.IsFailure)
            {
                return Result.Failure<ManualBookingResponse>(outcome.Error);
            }

            var booking = outcome.Value;
            await AfterCommitAsync(booking, cancellationToken);

            return Result.Success(new ManualBookingResponse(
                booking.Id,
                booking.BookingReference,
                booking.PitchId,
                booking.StartUtc,
                booking.EndUtc,
                booking.TotalPrice.Amount,
                booking.TotalPrice.Currency,
                command.CustomerName,
                booking.Status.ToString(),
                booking.DepositAmount.Amount,
                booking.HoldExpiresAtUtc));
        }

        _logger.LogError("Could not find an unused booking reference in {Attempts} attempts.", _maxReferenceAttempts);
        return Result.Failure<ManualBookingResponse>(BookingErrors.ReferenceUnavailable);
    }

    private async Task<(Result<Domain.Booking>? Outcome, bool Collided)> TryCreateAsync(
        CreateManualBookingCommand command, Guid staffUserId, Money price, ManualPayment payment, DateTime? holdExpiresAtUtc, DateTime nowUtc, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await BookingConcurrency.LockPitchAsync(_dbContext, command.PitchId, cancellationToken);
        await BookingConcurrency.ReleaseLapsedHoldsAsync(_dbContext, null, command.PitchId, command.StartUtc, command.EndUtc, nowUtc, cancellationToken);

        var bookingResult = Domain.Booking.CreateManual(
            IdGenerator.NewGuid(),
            _refGenerator.Generate(),
            command.VenueId,
            command.PitchId,
            staffUserId,
            command.CustomerName,
            command.CustomerPhone ?? "N/A",
            command.StartUtc,
            command.EndUtc,
            price,
            new Money(payment.DepositAmount, command.Currency), // EF cannot share one owned instance between two properties
            confirmedImmediately: payment.Confirmed,
            holdExpiresAtUtc: holdExpiresAtUtc,
            nowUtc: nowUtc,
            notes: command.Notes);
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

    private readonly record struct ManualPayment(bool Confirmed, decimal DepositAmount);

    /// <summary>
    /// Fully paid: confirmed, deposit equals the price. Deposit paid: confirmed with the amount received (more than zero, less
    /// than the price; the rest is due at the venue). Awaiting deposit: not confirmed, with the deposit the customer must send.
    /// </summary>
    private static Result<ManualPayment> ResolvePayment(CreateManualBookingCommand command)
    {
        if (!Enum.IsDefined(command.PaymentStatus))
        {
            return Result.Failure<ManualPayment>(Error.Validation("Booking.InvalidPaymentStatus", "Payment status must be FullyPaid, DepositPaid or AwaitingDeposit."));
        }

        if (command.PaymentStatus == ManualPaymentStatus.FullyPaid)
        {
            return Result.Success(new ManualPayment(true, command.Price));
        }

        var deposit = command.DepositAmount;
        if (deposit is not > 0)
        {
            return Result.Failure<ManualPayment>(Error.Validation("Booking.DepositAmountRequired", "Enter how much was paid, or how much deposit is expected."));
        }

        if (deposit > command.Price || (command.PaymentStatus == ManualPaymentStatus.DepositPaid && deposit >= command.Price))
        {
            return Result.Failure<ManualPayment>(Error.Validation(
                "Booking.DepositAmountInvalid", "The deposit must be more than zero and less than the price (use fully paid when the whole price was paid)."));
        }

        return Result.Success(new ManualPayment(command.PaymentStatus == ManualPaymentStatus.DepositPaid, deposit.Value));
    }

    /// <summary>The booking exists already: an outage in a side effect is logged, never returned as an error.</summary>
    private async Task AfterCommitAsync(Domain.Booking booking, CancellationToken cancellationToken)
    {
        await RunGuardedAsync(booking, "clear cached availability", () => _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken));
        await RunGuardedAsync(booking, "broadcast the booked slot", () => _broadcaster.BroadcastSlotStatusChangedAsync(
            booking.VenueId, booking.PitchId, booking.StartUtc, booking.EndUtc, "booked", cancellationToken));
    }

    private async Task RunGuardedAsync(Domain.Booking booking, string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Manual booking {BookingId} was created but the step '{Step}' failed.", booking.Id, step);
        }
    }
}
