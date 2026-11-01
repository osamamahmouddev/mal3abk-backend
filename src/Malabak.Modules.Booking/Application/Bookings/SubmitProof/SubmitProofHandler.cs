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

        // Duplicate receipt detection (TASK-408): Check if same SHA-256 hash was used on another booking
        if (!string.IsNullOrWhiteSpace(command.Sha256Hash))
        {
            var isDuplicate = await _dbContext.PaymentProofs.AsNoTracking()
                .AnyAsync(p => p.Sha256Hash == command.Sha256Hash
                            && p.BookingId != command.BookingId
                            && p.IsApproved != false,
                            cancellationToken);

            if (isDuplicate)
            {
                _logger.LogWarning("Duplicate payment proof hash {Hash} submitted for booking {BookingId}",
                    command.Sha256Hash, command.BookingId);
                return Result.Failure(Error.Conflict("Proof.DuplicateReceipt", "This payment receipt has already been submitted for another booking."));
            }
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
            command.FileKey,
            command.OriginalFileName,
            command.ContentType,
            command.FileSizeBytes,
            command.Sha256Hash ?? string.Empty,
            command.PlayerId.ToString("D"),
            nowUtc);

        booking.AddProof(proof);
        _dbContext.PaymentProofs.Add(proof);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Dispatch domain events
        await _domainEventDispatcher.DispatchAsync(booking.DomainEvents, cancellationToken);

        // Broadcast realtime update to player and venue staff
        await _broadcaster.BroadcastBookingUpdatedAsync(
            booking.PlayerId,
            booking.VenueId,
            booking.Id,
            booking.BookingReference,
            BookingStatus.ProofSubmitted.ToString(),
            nowUtc,
            cancellationToken);

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
}
