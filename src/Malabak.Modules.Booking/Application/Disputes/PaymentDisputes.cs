using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Disputes;

public sealed record DisputeEvidenceDto(Guid Id, string Side, Guid? AuthorId, string? Note, Guid? ProofId, DateTime CreatedAtUtc);

public sealed record DisputeDto(
    Guid Id, Guid BookingId, string BookingReference, string BookingStatus, Guid VenueId, Guid PlayerId, decimal DepositAmount, string OpenedBy,
    string Reason, string Status, Guid? ReviewerId, string? Outcome, string? ResolutionNote, DateTime CreatedAtUtc, DateTime? ResolvedAtUtc,
    IReadOnlyList<DisputeEvidenceDto> Evidence);

public sealed record DisputePage(IReadOnlyList<DisputeDto> Items, int Page, int PageSize, int Total);

/// <summary>
/// Payment proof disputes (TASK-704). Players and venues open disputes on their own bookings and add evidence; platform staff
/// work the queue and decide with a reason. The decision changes the booking (confirm, cancel with refund owed, reject) and is
/// audited; both sides are told.
/// </summary>
public sealed class PaymentDisputeService
{
    public const int MaxPageSize = 100;

    private readonly IBookingDbContext _dbContext;
    private readonly ICommandHandler<VenueCancelBookingCommand, Result> _cancel;
    private readonly IAvailabilityInvalidator _availability;
    private readonly IBookingRealtimeBroadcaster _broadcaster;
    private readonly IVenueDirectory _venues;
    private readonly INotificationPublisher _notifications;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public PaymentDisputeService(
        IBookingDbContext dbContext,
        ICommandHandler<VenueCancelBookingCommand, Result> cancel,
        IAvailabilityInvalidator availability,
        IBookingRealtimeBroadcaster broadcaster,
        IVenueDirectory venues,
        INotificationPublisher notifications,
        IAuditLog audit,
        IClock clock)
    {
        _dbContext = dbContext;
        _cancel = cancel;
        _availability = availability;
        _broadcaster = broadcaster;
        _venues = venues;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
    }

    // ---- opening and evidence (player / venue / system) ----

    public async Task<Result<DisputeDto>> OpenByPlayerAsync(Guid bookingId, Guid playerId, string? reason, CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.PlayerId == playerId && !b.IsDeleted, cancellationToken);
        return booking is null ? Result.Failure<DisputeDto>(BookingErrors.NotFound) : await OpenAsync(booking, DisputeOpenedBy.Player, playerId, reason, cancellationToken);
    }

    public async Task<Result<DisputeDto>> OpenByVenueAsync(Guid bookingId, Guid venueId, Guid staffId, string? reason, CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.VenueId == venueId && !b.IsDeleted, cancellationToken);
        return booking is null ? Result.Failure<DisputeDto>(BookingErrors.NotFound) : await OpenAsync(booking, DisputeOpenedBy.Venue, staffId, reason, cancellationToken);
    }

    /// <summary>For automatic checks (e.g. a flagged duplicate proof). No user is recorded as the opener.</summary>
    public async Task<Result<DisputeDto>> OpenBySystemAsync(Guid bookingId, string reason, CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted, cancellationToken);
        return booking is null ? Result.Failure<DisputeDto>(BookingErrors.NotFound) : await OpenAsync(booking, DisputeOpenedBy.System, null, reason, cancellationToken);
    }

    public async Task<Result<DisputeDto>> GetForPlayerAsync(Guid bookingId, Guid playerId, CancellationToken cancellationToken = default)
    {
        var dispute = await LatestAsync(d => d.BookingId == bookingId && d.PlayerId == playerId, cancellationToken);
        return dispute is null ? Result.Failure<DisputeDto>(DisputeErrors.NotFound) : Result.Success(await ToDtoAsync(dispute, cancellationToken));
    }

    public async Task<Result<DisputeDto>> GetForVenueAsync(Guid bookingId, Guid venueId, CancellationToken cancellationToken = default)
    {
        var dispute = await LatestAsync(d => d.BookingId == bookingId && d.VenueId == venueId, cancellationToken);
        return dispute is null ? Result.Failure<DisputeDto>(DisputeErrors.NotFound) : Result.Success(await ToDtoAsync(dispute, cancellationToken));
    }

    public async Task<Result<DisputeDto>> AddPlayerEvidenceAsync(Guid bookingId, Guid playerId, string? note, Guid? proofId, CancellationToken cancellationToken = default)
    {
        var dispute = await OpenDisputeAsync(d => d.BookingId == bookingId && d.PlayerId == playerId, cancellationToken);
        return await AddEvidenceAsync(dispute, DisputeOpenedBy.Player, playerId, note, proofId, cancellationToken);
    }

    public async Task<Result<DisputeDto>> AddVenueEvidenceAsync(Guid bookingId, Guid venueId, Guid staffId, string? note, Guid? proofId, CancellationToken cancellationToken = default)
    {
        var dispute = await OpenDisputeAsync(d => d.BookingId == bookingId && d.VenueId == venueId, cancellationToken);
        return await AddEvidenceAsync(dispute, DisputeOpenedBy.Venue, staffId, note, proofId, cancellationToken);
    }

    // ---- admin queue and decision ----

    public async Task<Result<DisputePage>> QueueAsync(string? status, Guid? venueId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        DisputeStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DisputeStatus>(status, ignoreCase: true, out var value) || !Enum.IsDefined(value))
            {
                return Result.Failure<DisputePage>(DisputeErrors.InvalidFilter);
            }

            parsed = value;
        }

        if (page < 1 || pageSize is < 1 or > MaxPageSize)
        {
            return Result.Failure<DisputePage>(DisputeErrors.InvalidFilter);
        }

        var disputes = _dbContext.PaymentDisputes.AsNoTracking().Include(d => d.Evidence).AsQueryable();
        disputes = parsed is { } s ? disputes.Where(d => d.Status == s) : disputes.Where(d => d.Status != DisputeStatus.Resolved);
        if (venueId is { } venue)
        {
            disputes = disputes.Where(d => d.VenueId == venue);
        }

        var total = await disputes.CountAsync(cancellationToken);
        var rows = await disputes.OrderBy(d => d.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = new List<DisputeDto>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(await ToDtoAsync(row, cancellationToken));
        }

        return Result.Success(new DisputePage(items, page, pageSize, total));
    }

    public async Task<Result<DisputeDto>> GetAsync(Guid disputeId, CancellationToken cancellationToken = default)
    {
        var dispute = await _dbContext.PaymentDisputes.AsNoTracking().Include(d => d.Evidence).FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);
        return dispute is null ? Result.Failure<DisputeDto>(DisputeErrors.NotFound) : Result.Success(await ToDtoAsync(dispute, cancellationToken));
    }

    public async Task<Result<DisputeDto>> StartReviewAsync(Guid disputeId, Guid adminId, CancellationToken cancellationToken = default)
    {
        var dispute = await _dbContext.PaymentDisputes.Include(d => d.Evidence).FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);
        if (dispute is null)
        {
            return Result.Failure<DisputeDto>(DisputeErrors.NotFound);
        }

        var started = dispute.StartReview(adminId, _clock.UtcNow);
        if (started.IsFailure)
        {
            return Result.Failure<DisputeDto>(started.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(dispute, cancellationToken));
    }

    public async Task<Result<DisputeDto>> AddAdminNoteAsync(Guid disputeId, Guid adminId, string? note, CancellationToken cancellationToken = default)
    {
        var dispute = await _dbContext.PaymentDisputes.Include(d => d.Evidence).FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);
        return await AddEvidenceAsync(dispute, DisputeOpenedBy.System, adminId, note, null, cancellationToken);
    }

    public async Task<Result<DisputeDto>> ResolveAsync(Guid disputeId, string? outcome, string? note, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<DisputeOutcome>(outcome, ignoreCase: true, out var decision) || !Enum.IsDefined(decision))
        {
            return Result.Failure<DisputeDto>(DisputeErrors.ReasonRequired);
        }

        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > PaymentDispute.MaxTextLength)
        {
            return Result.Failure<DisputeDto>(DisputeErrors.ReasonRequired);
        }

        var dispute = await _dbContext.PaymentDisputes.Include(d => d.Evidence).FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);
        if (dispute is null)
        {
            return Result.Failure<DisputeDto>(DisputeErrors.NotFound);
        }

        if (dispute.Status == DisputeStatus.Resolved)
        {
            return Result.Failure<DisputeDto>(DisputeErrors.AlreadyResolved);
        }

        var booking = await _dbContext.Bookings.Include(b => b.History).FirstAsync(b => b.Id == dispute.BookingId, cancellationToken);
        var before = booking.Status;
        var effect = await ApplyOutcomeAsync(booking, decision, note.Trim(), adminId, cancellationToken);
        if (effect.IsFailure)
        {
            return Result.Failure<DisputeDto>(effect.Error);
        }

        dispute.Resolve(decision, note, adminId, _clock.UtcNow);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<DisputeDto>(Error.Conflict("Booking.ConcurrentUpdate", "The booking changed while deciding. Reload and try again."));
        }

        if (booking.Status != before)
        {
            await _availability.InvalidatePitchAsync(booking.PitchId, cancellationToken);
            await _broadcaster.BroadcastBookingUpdatedAsync(booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference, booking.Status.ToString(), _clock.UtcNow, cancellationToken);
        }

        await _audit.RecordAsync(new AuditEntry("admin.dispute.resolved", nameof(PaymentDispute), dispute.Id.ToString("D"), adminId, AuditActorType.Admin,
            Before: new { BookingStatus = before.ToString() }, After: new { Outcome = decision.ToString(), BookingStatus = booking.Status.ToString() }, Reason: note.Trim()), cancellationToken);
        await NotifyBothSidesAsync(dispute, booking, NotificationTypes.PaymentDisputeResolved, new() { ["outcome"] = note.Trim() }, cancellationToken);
        return Result.Success(await ToDtoAsync(dispute, cancellationToken));
    }

    private async Task<Result> ApplyOutcomeAsync(Domain.Booking booking, DisputeOutcome decision, string note, Guid adminId, CancellationToken cancellationToken)
    {
        var actor = adminId.ToString("D");
        var now = _clock.UtcNow;
        var awaitingProof = booking.Status is BookingStatus.ProofSubmitted or BookingStatus.UnderReview;
        switch (decision)
        {
            case DisputeOutcome.ConfirmBooking:
                return awaitingProof ? booking.Confirm(actor, now) : Result.Failure(DisputeErrors.OutcomeNotApplicable);

            case DisputeOutcome.FavorVenue:
                return awaitingProof ? booking.Reject($"[Malabak] {note}", actor, now) : Result.Success();

            case DisputeOutcome.RefundPlayer:
                return await RefundPlayerAsync(booking, note, adminId, cancellationToken);

            default:
                return Result.Success();
        }
    }

    private async Task<Result> RefundPlayerAsync(Domain.Booking booking, string note, Guid adminId, CancellationToken cancellationToken)
    {
        if (booking.DepositAmount.Amount <= 0)
        {
            return Result.Failure(DisputeErrors.OutcomeNotApplicable);
        }

        if (booking.Status is BookingStatus.PendingDeposit or BookingStatus.ProofSubmitted or BookingStatus.UnderReview or BookingStatus.Confirmed)
        {
            // Same rules as a venue cancellation: the booking ends and the full deposit is owed back.
            var cancelled = await _cancel.HandleAsync(new VenueCancelBookingCommand(booking.Id, booking.VenueId, adminId, $"[Malabak] {note}"), cancellationToken);
            if (cancelled.IsFailure)
            {
                return cancelled;
            }

            // The handler works on the same tracked booking instance, so `booking` already shows the cancellation.
            return Result.Success();
        }

        var refund = await _dbContext.BookingRefunds.FirstOrDefaultAsync(r => r.BookingId == booking.Id, cancellationToken);
        if (refund is null)
        {
            _dbContext.BookingRefunds.Add(BookingRefund.CreatePending(
                Guid.NewGuid(), booking.Id, booking.DepositAmount.Amount, booking.DepositAmount.Currency, note, "Dispute decision: full deposit refund.", _clock.UtcNow));
            return Result.Success();
        }

        return refund.Status == RefundStatus.Pending
            ? Result.Success()
            : refund.OverrideToPending(booking.DepositAmount.Amount, note, adminId.ToString("D"), _clock.UtcNow);
    }

    private async Task<Result<DisputeDto>> OpenAsync(Domain.Booking booking, DisputeOpenedBy by, Guid? userId, string? reason, CancellationToken cancellationToken)
    {
        if (await _dbContext.PaymentDisputes.AnyAsync(d => d.BookingId == booking.Id && d.Status != DisputeStatus.Resolved, cancellationToken))
        {
            return Result.Failure<DisputeDto>(DisputeErrors.AlreadyOpen);
        }

        var opened = PaymentDispute.Open(booking, by, userId, reason, _clock.UtcNow);
        if (opened.IsFailure)
        {
            return Result.Failure<DisputeDto>(opened.Error);
        }

        _dbContext.PaymentDisputes.Add(opened.Value);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two requests raced past the check; the filtered unique index kept only one.
            return Result.Failure<DisputeDto>(DisputeErrors.AlreadyOpen);
        }

        await _audit.RecordAsync(new AuditEntry("dispute.opened", nameof(PaymentDispute), opened.Value.Id.ToString("D"), userId,
            by == DisputeOpenedBy.System ? AuditActorType.System : AuditActorType.User, After: new { BookingId = booking.Id, OpenedBy = by.ToString() }), cancellationToken);
        await NotifyBothSidesAsync(opened.Value, booking, NotificationTypes.PaymentDisputeOpened, [], cancellationToken);
        return Result.Success(await ToDtoAsync(opened.Value, cancellationToken));
    }

    private async Task<Result<DisputeDto>> AddEvidenceAsync(
        PaymentDispute? dispute, DisputeOpenedBy side, Guid authorId, string? note, Guid? proofId, CancellationToken cancellationToken)
    {
        if (dispute is null)
        {
            return Result.Failure<DisputeDto>(DisputeErrors.NotFound);
        }

        if (proofId is { } proof && !await _dbContext.PaymentProofs.AnyAsync(p => p.Id == proof && p.BookingId == dispute.BookingId, cancellationToken))
        {
            return Result.Failure<DisputeDto>(DisputeErrors.InvalidEvidence);
        }

        var evidence = dispute.AddEvidence(side, authorId, note, proofId, _clock.UtcNow);
        if (evidence.IsFailure)
        {
            return Result.Failure<DisputeDto>(evidence.Error);
        }

        _dbContext.DisputeEvidence.Add(evidence.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(dispute, cancellationToken));
    }

    private async Task NotifyBothSidesAsync(PaymentDispute dispute, Domain.Booking booking, string type, Dictionary<string, string> data, CancellationToken cancellationToken)
    {
        data["bookingId"] = booking.Id.ToString("D");
        data["bookingReference"] = booking.BookingReference;
        var recipients = new HashSet<Guid> { booking.PlayerId };
        recipients.UnionWith(await _venues.GetStaffUserIdsAsync(booking.VenueId, Permissions.PaymentProofsReview, cancellationToken));
        foreach (var userId in recipients)
        {
            await _notifications.PublishAsync(new NotificationRequest(userId, type, data, $"{type}:{dispute.Id:N}:{userId:N}"), cancellationToken);
        }
    }

    private Task<PaymentDispute?> LatestAsync(System.Linq.Expressions.Expression<Func<PaymentDispute, bool>> filter, CancellationToken cancellationToken) =>
        _dbContext.PaymentDisputes.AsNoTracking().Include(d => d.Evidence).Where(filter).OrderByDescending(d => d.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);

    private Task<PaymentDispute?> OpenDisputeAsync(System.Linq.Expressions.Expression<Func<PaymentDispute, bool>> filter, CancellationToken cancellationToken) =>
        _dbContext.PaymentDisputes.Include(d => d.Evidence).Where(filter).Where(d => d.Status != DisputeStatus.Resolved).FirstOrDefaultAsync(cancellationToken);

    private async Task<DisputeDto> ToDtoAsync(PaymentDispute d, CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Bookings.AsNoTracking().Where(b => b.Id == d.BookingId)
            .Select(b => new { b.BookingReference, b.Status, Deposit = b.DepositAmount.Amount }).FirstAsync(cancellationToken);
        return new DisputeDto(d.Id, d.BookingId, booking.BookingReference, booking.Status.ToString(), d.VenueId, d.PlayerId, booking.Deposit, d.OpenedBy.ToString(),
            d.Reason, d.Status.ToString(), d.ReviewerId, d.Outcome?.ToString(), d.ResolutionNote, d.CreatedAtUtc, d.ResolvedAtUtc,
            d.Evidence.OrderBy(e => e.CreatedAtUtc).Select(e => new DisputeEvidenceDto(e.Id, e.Side.ToString(), e.AuthorId, e.Note, e.ProofId, e.CreatedAtUtc)).ToList());
    }
}
