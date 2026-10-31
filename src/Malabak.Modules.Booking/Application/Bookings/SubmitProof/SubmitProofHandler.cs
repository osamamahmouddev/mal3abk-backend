using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.SubmitProof;

public sealed class SubmitProofHandler : ICommandHandler<SubmitProofCommand, Result>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly INotificationPublisher? _notificationPublisher;
    private readonly ILogger<SubmitProofHandler> _logger;

    public SubmitProofHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IBookingRealtimeBroadcaster broadcaster,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<SubmitProofHandler> logger,
        INotificationPublisher? notificationPublisher = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _broadcaster = broadcaster;
        _domainEventDispatcher = domainEventDispatcher;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(SubmitProofCommand command, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var booking = await _dbContext.Bookings
            .Include(b => b.Proofs)
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == command.BookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        // Ownership enforcement
        if (booking.PlayerId != command.PlayerId)
        {
            return Result.Failure(Error.Forbidden("Booking.Forbidden", "You do not own this booking."));
        }

        // Hold expiration check
        if (booking.HoldExpiresAtUtc <= nowUtc)
        {
            return Result.Failure(BookingErrors.HoldExpired);
        }

        // Duplicate receipt detection (TASK-408). A proof without a content hash cannot be checked, so it is refused
        // instead of being let through unchecked.
        if (string.IsNullOrWhiteSpace(command.Sha256Hash))
        {
            return Result.Failure(Error.Validation("Proof.HashRequired", "The receipt must have a content hash."));
        }

        var isDuplicate = await _dbContext.PaymentProofs.AsNoTracking()
            .AnyAsync(p => p.Sha256Hash == command.Sha256Hash
                        && p.BookingId != command.BookingId
                        && p.IsApproved != false,
                        cancellationToken);

        if (isDuplicate)
        {
            _logger.LogWarning("Duplicate payment proof submitted for booking {BookingId}", command.BookingId);
            return Result.Failure(_duplicateReceipt);
        }

        // Aggregate transition to ProofSubmitted
        var proofId = Guid.NewGuid();
        var submitResult = booking.SubmitProof(
            proofId,
            command.PlayerId.ToString("D"),
            nowUtc);

        if (submitResult.IsFailure)
        {
            return submitResult;
        }

        // Create the PaymentProof entity and attach to booking
        var proof = PaymentProof.Create(
            proofId,
            booking.Id,
            command.MediaId,
            command.FileKey,
            command.OriginalFileName,
            command.ContentType,
            command.FileSizeBytes,
            command.Sha256Hash ?? string.Empty,
            command.PlayerId.ToString("D"),
            nowUtc);

        booking.AddProof(proof);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (BookingPersistenceErrors.IsDuplicateProofHash(ex))
        {
            // Two requests carried the same receipt at once and both passed the check above; the index let one through.
            _logger.LogWarning(ex, "Duplicate payment proof submitted for booking {BookingId} (concurrent)", command.BookingId);
            return Result.Failure(_duplicateReceipt);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The booking changed while this request was running (for example a second submit, a cancel or an expiry).
            return Result.Failure(Error.Conflict("Booking.ConcurrentUpdate", "The booking was changed by another request. Please reload and try again."));
        }

        // The proof is saved. A failing side effect must not turn that into an error the player would retry.
        await RunGuardedAsync(booking, "dispatch domain events", () => _domainEventDispatcher.DispatchAsync(booking.DomainEvents, cancellationToken));
        await RunGuardedAsync(booking, "broadcast the booking update", () => _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId,
            booking.VenueId,
            booking.Id,
            booking.BookingReference,
            BookingStatus.ProofSubmitted.ToString(),
            nowUtc,
            cancellationToken));

        // Dispatch notification
        if (_notificationPublisher is not null)
        {
            var notif = new NotificationRequest(
                booking.VenueId, // Venue staff listener
                NotificationTypes.PaymentProofSubmitted,
                new Dictionary<string, string>
                {
                    ["BookingId"] = booking.Id.ToString("D"),
                    ["BookingReference"] = booking.BookingReference,
                    ["DepositAmount"] = booking.DepositAmount.Amount.ToString("F2"),
                    ["Currency"] = booking.DepositAmount.Currency
                },
                $"proof-submitted:{booking.Id:D}");

            try
            {
                await _notificationPublisher.PublishAsync(notif, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish payment proof submitted notification for booking {BookingId}", booking.Id);
            }
        }

        return Result.Success();
    }

    private static readonly Error _duplicateReceipt = Error.Conflict("Proof.DuplicateReceipt", "This payment receipt has already been submitted for another booking.");

    private async Task RunGuardedAsync(Domain.Booking booking, string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Proof for booking {BookingId} was saved but the step '{Step}' failed.", booking.Id, step);
        }
    }
}
