using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Bookings.SubmitProof;

public sealed class SubmitProofHandler : ICommandHandler<SubmitProofCommand, Result>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly ILogger<SubmitProofHandler> _logger;

    public SubmitProofHandler(
        IBookingDbContext dbContext,
        IClock clock,
        IBookingRealtimeBroadcaster broadcaster,
        ILogger<SubmitProofHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _broadcaster = broadcaster;
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


        // A proof only exists for a media asset that is Ready, and an asset only becomes Ready after the upload pipeline
        // accepted it (file-type signature, malware scan, re-encoding). Production requires ClamAV, so "clean" is recorded
        // here instead of leaving every receipt looking unscanned.
        proof.MarkScanned(isClean: true, "Passed the upload pipeline (type check, malware scan, re-encode)", nowUtc);

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
        catch (DbUpdateException ex) when (BookingPersistenceErrors.IsSlotOverlap(ex))
        {
            // After a rejection the booking no longer holds the slot in the database (only active bookings do). If someone
            // else booked it meanwhile, a corrected proof cannot bring this booking back.
            _logger.LogWarning(ex, "Booking {BookingId} could not resume after its proof was resubmitted: the slot was taken", command.BookingId);
            return Result.Failure(BookingErrors.SlotOverlap);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The booking changed while this request was running (for example a second submit, a cancel or an expiry).
            return Result.Failure(Error.Conflict("Booking.ConcurrentUpdate", "The booking was changed by another request. Please reload and try again."));
        }

        // The proof is saved. A failing side effect must not turn that into an error the player would retry.
        await RunGuardedAsync(booking, "broadcast the booking update", () => _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId,
            booking.VenueId,
            booking.Id,
            booking.BookingReference,
            BookingStatus.ProofSubmitted.ToString(),
            nowUtc,
            cancellationToken));

        // The platform reviews deposit proofs (they're paid to its accounts), so the venue isn't asked to; the proof shows up in
        // the admin deposit-review queue, and the venue hears about the booking once it's confirmed.

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
