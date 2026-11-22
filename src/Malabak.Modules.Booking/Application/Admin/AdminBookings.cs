using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Admin;

public sealed record AdminBookingSearch(
    string? Reference, Guid? VenueId, Guid? PlayerId, string? Status, DateTime? FromUtc, DateTime? ToUtc, int Page = 1, int PageSize = 25);

public sealed record AdminBookingListItem(
    Guid Id, string Reference, Guid VenueId, Guid PitchId, Guid PlayerId, string Status, DateTime StartUtc, DateTime EndUtc,
    decimal TotalPrice, decimal DepositAmount, string Currency, bool IsManual, DateTime CreatedAtUtc,
    decimal PlatformCommission = 0m, decimal VenueNetAmount = 0m, decimal OnlinePaidAmount = 0m, decimal VenuePayAmount = 0m, bool IsCommissionPaid = false,
    string? VenueName = null, string? PitchName = null, string? CustomerName = null, string? CustomerPhone = null);

public sealed record AdminBookingPage(IReadOnlyList<AdminBookingListItem> Items, int Page, int PageSize, int Total);

public sealed record AdminBookingHistoryItem(string FromStatus, string ToStatus, string? Reason, string ChangedBy, DateTime ChangedAtUtc);

/// <summary>Proof metadata and short-lived signed download link for staff review.</summary>
public sealed record AdminProofItem(
    Guid Id, string ContentType, long FileSizeBytes, DateTime UploadedAtUtc, bool IsScanClean, bool? IsApproved, string? RejectionReason,
    DateTime? ReviewedAtUtc, string? ReviewedBy, bool IsArchived,
    Guid? MediaId = null, string? OriginalFileName = null, string? DownloadUrl = null);

public sealed record AdminRefundItem(Guid Id, string Status, decimal ExpectedAmount, decimal? ActualAmount, string Currency, string? Reason, DateTime? RecordedAtUtc);

public sealed record AdminBookingDetail(
    AdminBookingListItem Booking,
    string? CancellationReason,
    string? RejectionReason,
    DateTime? HoldExpiresAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc,
    string? CustomerName,
    string? Notes,
    BookingPolicySnapshot? Policy,
    IReadOnlyList<AdminBookingHistoryItem> History,
    IReadOnlyList<AdminProofItem> Proofs,
    IReadOnlyList<AdminRefundItem> Refunds,
    string? CustomerPhone = null);

public sealed record AdminDepositReviewItem(
    Guid BookingId,
    string Reference,
    Guid VenueId,
    Guid PitchId,
    Guid PlayerId,
    decimal TotalPrice,
    decimal DepositAmount,
    string Currency,
    string Status,
    DateTime? HoldExpiresAtUtc,
    DateTime CreatedAtUtc,
    Guid? LatestProofId,
    Guid? MediaId,
    string? OriginalFileName,
    string? ContentType,
    long? FileSizeBytes,
    DateTime? ProofUploadedAtUtc,
    string? RejectionReason,
    string? VenueName = null,
    string? PitchName = null,
    string? DownloadUrl = null,
    string? CustomerName = null,
    string? CustomerPhone = null);

public sealed record AdminDepositReviewPage(
    IReadOnlyList<AdminDepositReviewItem> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record VenueFinancialSummaryItem(
    Guid VenueId,
    string VenueName,
    int TotalBookings,
    int ConfirmedBookings,
    int CancelledBookings,
    int PendingConfirmationBookings,
    decimal TotalGrossAmount,
    decimal TotalDepositAmount,
    decimal TotalPlatformCommission,
    decimal TotalVenueNetAmount,
    decimal TotalDueAtVenueCash,
    int PendingRefundsCount,
    decimal PendingRefundsAmount,
    decimal VenueDepositShare = 0m,
    string Currency = "EGP",
    int ManualBookings = 0,
    decimal PlatformCollectedOnline = 0m,
    decimal CommissionCollected = 0m,
    decimal CommissionOutstanding = 0m,
    decimal PlatformOwesVenue = 0m,
    string? CommissionType = null,
    decimal? CommissionRate = null,
    bool CustomCommissionEnabled = false);

public static class AdminBookingErrors
{
    public static readonly Error InvalidFilter = Error.Validation("AdminBookings.InvalidFilter", "Invalid booking search filter.");
}

/// <summary>
/// Cross-venue booking monitoring for platform staff (TASK-703): search, full detail with status, payment, proof and refund
/// history, centralized deposit review, and an audited force-cancel that follows the venue-cancellation rules.
/// </summary>
public sealed class AdminBookingService
{
    public const int MaxPageSize = 100;
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);

    private readonly IBookingDbContext _dbContext;
    private readonly ICommandHandler<VenueCancelBookingCommand, Result> _cancel;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;
    private readonly IBookingRealtimeBroadcaster? _broadcaster;
    private readonly IAvailabilityInvalidator? _availabilityInvalidator;
    private readonly IVenueDirectory? _venues;
    private readonly INotificationPublisher? _notificationPublisher;
    private readonly IUserAccountsContract? _users;
    private readonly ILogger<AdminBookingService>? _logger;

    public AdminBookingService(
        IBookingDbContext dbContext,
        ICommandHandler<VenueCancelBookingCommand, Result> cancel,
        IAuditLog audit,
        IClock? clock = null,
        IBookingRealtimeBroadcaster? broadcaster = null,
        IAvailabilityInvalidator? availabilityInvalidator = null,
        IVenueDirectory? venues = null,
        INotificationPublisher? notificationPublisher = null,
        IUserAccountsContract? users = null,
        ILogger<AdminBookingService>? logger = null)
    {
        _dbContext = dbContext;
        _cancel = cancel;
        _audit = audit;
        _clock = clock ?? new SystemClock();
        _broadcaster = broadcaster;
        _availabilityInvalidator = availabilityInvalidator;
        _venues = venues;
        _notificationPublisher = notificationPublisher;
        _users = users;
        _logger = logger;
    }

    public async Task<Result<AdminBookingPage>> SearchAsync(AdminBookingSearch search, CancellationToken cancellationToken = default)
    {
        BookingStatus? status = null;
        if (search.Status is not null)
        {
            if (!Enum.TryParse<BookingStatus>(search.Status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Result.Failure<AdminBookingPage>(AdminBookingErrors.InvalidFilter);
            }
            status = parsed;
        }

        if (search.Page < 1 || search.PageSize is < 1 or > MaxPageSize || search.Reference?.Length > 40
            || (search.FromUtc is { } from && search.ToUtc is { } to && (to < from || to - from > MaxRange)))
        {
            return Result.Failure<AdminBookingPage>(AdminBookingErrors.InvalidFilter);
        }

        var query = _dbContext.Bookings.AsNoTracking().Where(b => !b.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search.Reference))
        {
            var reference = search.Reference.Trim().ToUpperInvariant();
            query = query.Where(b => b.BookingReference.Contains(reference));
        }
        if (search.VenueId.HasValue)
        {
            query = query.Where(b => b.VenueId == search.VenueId.Value);
        }
        if (search.PlayerId.HasValue)
        {
            query = query.Where(b => b.PlayerId == search.PlayerId.Value);
        }
        if (status.HasValue)
        {
            query = query.Where(b => b.Status == status.Value);
        }
        if (search.FromUtc.HasValue)
        {
            query = query.Where(b => b.StartUtc >= search.FromUtc.Value);
        }
        if (search.ToUtc.HasValue)
        {
            query = query.Where(b => b.StartUtc <= search.ToUtc.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var page = search.Page < 1 ? 1 : search.Page;
        var pageSize = search.PageSize is < 1 or > MaxPageSize ? 25 : search.PageSize;

        var rows = await query
            .OrderByDescending(b => b.StartUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var venueIds = rows.Select(b => b.VenueId).Distinct().ToList();
        var pitchIds = rows.Select(b => b.PitchId).Distinct().ToList();
        var venueNames = _venues is not null ? await _venues.GetVenueNamesAsync(venueIds, cancellationToken) : new Dictionary<Guid, string>();
        var pitchNames = _venues is not null ? await _venues.GetPitchNamesAsync(pitchIds, cancellationToken) : new Dictionary<Guid, string>();
        var venueCommissions = _venues is not null ? await _venues.GetVenueCommissionsAsync(venueIds, cancellationToken) : new Dictionary<Guid, VenueCommissionInfo>();

        var playerIds = rows.Where(b => !b.IsManualBooking && (string.IsNullOrWhiteSpace(b.CustomerName) || string.IsNullOrWhiteSpace(b.CustomerPhone)))
            .Select(b => b.PlayerId).Distinct().ToList();
        var playerSummaries = _users is not null && playerIds.Count > 0
            ? await _users.GetSummariesAsync(playerIds, cancellationToken)
            : new Dictionary<Guid, UserAccountSummary>();

        return Result.Success(new AdminBookingPage(
            rows.Select(b =>
            {
                playerSummaries.TryGetValue(b.PlayerId, out var s);
                var cName = string.IsNullOrWhiteSpace(b.CustomerName) ? s?.FullName : b.CustomerName;
                var cPhone = string.IsNullOrWhiteSpace(b.CustomerPhone) ? s?.MaskedPhone : b.CustomerPhone;
                venueCommissions.TryGetValue(b.VenueId, out var comm);
                return ToItem(b, venueNames.GetValueOrDefault(b.VenueId), pitchNames.GetValueOrDefault(b.PitchId), cName, cPhone, comm);
            }).ToList(),
            page, pageSize, total));
    }

    public async Task<Result<AdminBookingDetail>> GetAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.AsNoTracking()
            .Include(b => b.Proofs)
            .Include(b => b.Refunds)
            .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted, cancellationToken);
        if (booking is null)
        {
            return Result.Failure<AdminBookingDetail>(BookingErrors.NotFound);
        }

        var history = await _dbContext.StatusHistories.AsNoTracking()
            .Where(h => h.BookingId == bookingId)
            .OrderBy(h => h.ChangedAtUtc)
            .ToListAsync(cancellationToken);

        string? venueName = null;
        string? pitchName = null;
        VenueCommissionInfo? venueComm = null;
        if (_venues is not null)
        {
            var vNames = await _venues.GetVenueNamesAsync([booking.VenueId], cancellationToken);
            venueName = vNames.GetValueOrDefault(booking.VenueId);
            var pNames = await _venues.GetPitchNamesAsync([booking.PitchId], cancellationToken);
            pitchName = pNames.GetValueOrDefault(booking.PitchId);
            var vComms = await _venues.GetVenueCommissionsAsync([booking.VenueId], cancellationToken);
            venueComm = vComms.GetValueOrDefault(booking.VenueId);
        }

        string? customerName = booking.CustomerName;
        string? customerPhone = booking.CustomerPhone;
        if (_users is not null && !booking.IsManualBooking && (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerPhone)))
        {
            var summaries = await _users.GetSummariesAsync([booking.PlayerId], cancellationToken);
            if (summaries.TryGetValue(booking.PlayerId, out var summary))
            {
                customerName = string.IsNullOrWhiteSpace(customerName) ? summary.FullName : customerName;
                customerPhone = string.IsNullOrWhiteSpace(customerPhone) ? summary.MaskedPhone : customerPhone;
            }
        }

        return Result.Success(new AdminBookingDetail(
            ToItem(booking, venueName, pitchName, customerName, customerPhone, venueComm),
            booking.CancellationReason,
            booking.RejectionReason,
            booking.HoldExpiresAtUtc,
            booking.ConfirmedAtUtc,
            booking.CancelledAtUtc,
            customerName,
            booking.Notes,
            booking.PolicySnapshot,
            history.Select(h => new AdminBookingHistoryItem(h.FromStatus.ToString(), h.ToStatus.ToString(), h.Reason, h.ChangedBy, h.ChangedAtUtc)).ToList(),
            booking.Proofs.OrderByDescending(p => p.UploadedAtUtc).Select(p => new AdminProofItem(
                p.Id, p.ContentType, p.FileSizeBytes, p.UploadedAtUtc, p.IsScanClean, p.IsApproved, p.RejectionReason, p.ReviewedAtUtc, p.ReviewedByUserId, p.IsArchived,
                p.MediaId, p.OriginalFileName)).ToList(),
            booking.Refunds.OrderByDescending(r => r.RecordedAtUtc).Select(r => new AdminRefundItem(
                r.Id, r.Status.ToString(), r.ExpectedRefundAmount, r.ActualRefundAmount, r.Currency, r.Reason, r.RecordedAtUtc)).ToList(),
            customerPhone));
    }

    /// <summary>Admin intervention: cancels regardless of the player deadline, owes the full deposit back and is audited.</summary>
    public async Task<Result<AdminBookingDetail>> ForceCancelAsync(Guid bookingId, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<AdminBookingDetail>(BookingErrors.ReasonRequired);
        }

        var booking = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.Id == bookingId && !b.IsDeleted).Select(b => new { b.VenueId, b.Status }).FirstOrDefaultAsync(cancellationToken);
        if (booking is null)
        {
            return Result.Failure<AdminBookingDetail>(BookingErrors.NotFound);
        }

        // The player sees the reason; the prefix makes clear the platform, not the venue, cancelled.
        var cancelled = await _cancel.HandleAsync(new VenueCancelBookingCommand(bookingId, booking.VenueId, adminId, $"[Malabak] {reason.Trim()}"), cancellationToken);
        if (cancelled.IsFailure)
        {
            return Result.Failure<AdminBookingDetail>(cancelled.Error);
        }

        await _audit.RecordAsync(new AuditEntry("admin.booking.force_cancelled", "Booking", bookingId.ToString("D"), adminId, AuditActorType.Admin,
            Before: new { Status = booking.Status.ToString() }, After: new { Status = nameof(BookingStatus.Cancelled) }, Reason: reason), cancellationToken);
        return await GetAsync(bookingId, cancellationToken);
    }

    /// <summary>Returns bookings awaiting central platform deposit proof verification.</summary>
    public async Task<Result<AdminDepositReviewPage>> GetPendingDepositReviewsAsync(
        int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var p = page < 1 ? 1 : page;
        var ps = pageSize is < 1 or > MaxPageSize ? 25 : pageSize;

        var query = _dbContext.Bookings.AsNoTracking()
            .Include(b => b.Proofs)
            .Where(b => !b.IsDeleted && (b.Status == BookingStatus.ProofSubmitted || b.Status == BookingStatus.UnderReview));

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(b => b.CreatedAtUtc)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(cancellationToken);

        var venueIds = rows.Select(b => b.VenueId).Distinct().ToList();
        var pitchIds = rows.Select(b => b.PitchId).Distinct().ToList();
        var venueNames = _venues is not null ? await _venues.GetVenueNamesAsync(venueIds, cancellationToken) : new Dictionary<Guid, string>();
        var pitchNames = _venues is not null ? await _venues.GetPitchNamesAsync(pitchIds, cancellationToken) : new Dictionary<Guid, string>();

        var playerIds = rows.Select(b => b.PlayerId).Distinct().ToList();
        var playerSummaries = _users is not null && playerIds.Count > 0
            ? await _users.GetSummariesAsync(playerIds, cancellationToken)
            : new Dictionary<Guid, UserAccountSummary>();

        var items = rows.Select(b =>
        {
            var latestProof = b.Proofs.OrderByDescending(pr => pr.UploadedAtUtc).FirstOrDefault();
            playerSummaries.TryGetValue(b.PlayerId, out var pSummary);
            var cName = string.IsNullOrWhiteSpace(b.CustomerName) ? pSummary?.FullName : b.CustomerName;
            var cPhone = string.IsNullOrWhiteSpace(b.CustomerPhone) ? pSummary?.MaskedPhone : b.CustomerPhone;
            return new AdminDepositReviewItem(
                b.Id,
                b.BookingReference,
                b.VenueId,
                b.PitchId,
                b.PlayerId,
                b.TotalPrice.Amount,
                b.DepositAmount.Amount,
                b.TotalPrice.Currency,
                b.Status.ToString(),
                b.HoldExpiresAtUtc,
                b.CreatedAtUtc,
                latestProof?.Id,
                latestProof?.MediaId,
                latestProof?.OriginalFileName,
                latestProof?.ContentType,
                latestProof?.FileSizeBytes,
                latestProof?.UploadedAtUtc,
                b.RejectionReason,
                venueNames.GetValueOrDefault(b.VenueId),
                pitchNames.GetValueOrDefault(b.PitchId),
                null,
                cName,
                cPhone);
        }).ToList();

        return Result.Success(new AdminDepositReviewPage(items, p, ps, total));
    }

    /// <summary>Admin decision on a submitted deposit proof: confirms booking or rejects with reason.</summary>
    public async Task<Result> ReviewDepositAsync(
        Guid bookingId, bool approved, string? rejectionReason, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var actor = adminUserId.ToString("D");

        var booking = await _dbContext.Bookings
            .Include(b => b.Proofs)
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        var reason = rejectionReason?.Trim();
        if (!approved)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Result.Failure(BookingErrors.RejectionReasonRequired);
            }

            if (reason.Length > 500)
            {
                return Result.Failure(Error.Validation("Booking.RejectionReasonTooLong", "The rejection reason cannot exceed 500 characters."));
            }
        }

        Result decision;
        if (approved)
        {
            if (!booking.IsManualBooking && booking.Status is BookingStatus.ProofSubmitted or BookingStatus.UnderReview)
            {
                decision = booking.ConfirmPlatformDeposit(actor, nowUtc);
            }
            else if (booking.Status is BookingStatus.ProofSubmitted or BookingStatus.UnderReview)
            {
                decision = booking.Confirm(actor, nowUtc);
            }
            else if (booking.IsManualBooking && booking.Status == BookingStatus.PendingDeposit)
            {
                decision = booking.Confirm(actor, nowUtc);
            }
            else if (booking.Status == BookingStatus.PendingDeposit)
            {
                decision = booking.ConfirmOnlinePayment("ADMIN", "ADMIN_CONFIRMED", booking.DepositAmount, nowUtc, actor);
            }
            else
            {
                decision = booking.Confirm(actor, nowUtc);
            }
        }
        else
        {
            decision = booking.Reject(reason!, actor, nowUtc);
        }

        if (decision.IsFailure)
        {
            return decision;
        }

        var latestProof = booking.Proofs.OrderByDescending(p => p.UploadedAtUtc).FirstOrDefault();
        latestProof?.Review(approved, approved ? null : reason, actor, nowUtc);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Error.Conflict("Booking.ConcurrentUpdate", "The booking was changed by another request. Please reload and try again."));
        }

        await _audit.RecordAsync(new AuditEntry(
            approved ? "admin.booking.deposit_approved" : "admin.booking.deposit_rejected",
            "Booking",
            bookingId.ToString("D"),
            adminUserId,
            AuditActorType.Admin,
            Before: new { Status = BookingStatus.ProofSubmitted.ToString() },
            After: new { Status = booking.Status.ToString(), Reason = reason },
            Reason: reason), cancellationToken);

        if (approved && _availabilityInvalidator is not null)
        {
            await _availabilityInvalidator.InvalidatePitchAsync(booking.PitchId, cancellationToken);
        }

        if (approved && _broadcaster is not null)
        {
            await _broadcaster.BroadcastSlotStatusChangedAsync(
                booking.VenueId, booking.PitchId, booking.StartUtc, booking.EndUtc, "Confirmed", cancellationToken);
        }

        if (_broadcaster is not null)
        {
            await _broadcaster.BroadcastBookingUpdatedAsync(
                booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference,
                booking.Status.ToString(), nowUtc, cancellationToken);
        }

        if (_notificationPublisher is not null && _venues is not null)
        {
            try
            {
                var names = await _venues.GetVenueNamesAsync([booking.VenueId], cancellationToken);
                var venueName = names.GetValueOrDefault(booking.VenueId, string.Empty);

                var notification = approved
                    ? BookingNotifications.Confirmed(booking, venueName, _clock)
                    : BookingNotifications.Rejected(booking, venueName, reason ?? string.Empty, _clock);

                await _notificationPublisher.PublishAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to publish notification for booking {BookingId}", booking.Id);
            }
        }

        return Result.Success();
    }

    /// <summary>Marks a booking as UnderReview when viewed by admin staff.</summary>
    public async Task<Result> MarkUnderReviewAsync(Guid bookingId, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var actor = adminUserId.ToString("D");

        var booking = await _dbContext.Bookings
            .FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure(BookingErrors.NotFound);
        }

        if (booking.Status == BookingStatus.ProofSubmitted)
        {
            var marked = booking.MarkUnderReview(actor, nowUtc);
            if (marked.IsSuccess)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (_broadcaster is not null)
                {
                    await _broadcaster.BroadcastBookingUpdatedAsync(
                        booking.PlayerId, booking.VenueId, booking.Id, booking.BookingReference,
                        BookingStatus.UnderReview.ToString(), nowUtc, cancellationToken);
                }
            }
        }

        return Result.Success();
    }

    /// <summary>Calculates comprehensive financial and commission breakdowns per venue.</summary>
    public async Task<Result<IReadOnlyList<VenueFinancialSummaryItem>>> GetVenuesFinancialSummaryAsync(
        DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Bookings.AsNoTracking().Where(b => !b.IsDeleted && b.Status != BookingStatus.Draft);
        if (fromUtc.HasValue)
        {
            query = query.Where(b => b.StartUtc >= fromUtc.Value);
        }
        if (toUtc.HasValue)
        {
            query = query.Where(b => b.StartUtc <= toUtc.Value);
        }

        var bookings = await query
            .Select(b => new
            {
                b.Id,
                b.VenueId,
                b.Status,
                b.StartUtc,
                b.EndUtc,
                b.IsManualBooking,
                TotalPrice = b.TotalPrice.Amount,
                DepositAmount = b.DepositAmount.Amount,
                PlatformCommission = b.PlatformCommission.Amount,
                OnlinePaidAmount = b.OnlinePaidAmount.Amount,
                HasPriceSnapshot = b.PriceSnapshotJson != null,
            })
            .ToListAsync(cancellationToken);

        var pendingRefunds = await _dbContext.BookingRefunds.AsNoTracking()
            .Where(r => r.Status == RefundStatus.Pending)
            .Select(r => new { r.BookingId, r.ExpectedRefundAmount })
            .ToListAsync(cancellationToken);

        var bookingVenueMap = bookings.ToDictionary(b => b.Id, b => b.VenueId);
        var venueIds = bookings.Select(b => b.VenueId).Distinct().ToList();

        var venueNames = _venues is not null
            ? await _venues.GetVenueNamesAsync(venueIds, cancellationToken)
            : new Dictionary<Guid, string>();

        var venueCommissions = _venues is not null
            ? await _venues.GetVenueCommissionsAsync(venueIds, cancellationToken)
            : new Dictionary<Guid, VenueCommissionInfo>();

        // Money only counts once a booking is confirmed or played. The deposit of a booking still waiting for its proof has
        // not been paid yet, so it is never reported as collected. Per confirmed booking:
        //   commission C (0 for a venue's own manual booking), online paid O (Paymob, held by the platform),
        //   venue net = total - C, cash at the venue = total - deposit,
        //   platform owes the venue max(0, O - C), the venue owes the platform the commission O did not cover.
        var summary = venueIds.Select(vId =>
        {
            var vBookings = bookings.Where(b => b.VenueId == vId).ToList();
            var confirmed = vBookings.Where(b => b.Status is BookingStatus.Confirmed or BookingStatus.Completed).ToList();
            var cancelled = vBookings.Where(b => b.Status == BookingStatus.Cancelled).ToList();
            var pending = vBookings.Where(b => b.Status is BookingStatus.PendingDeposit or BookingStatus.ProofSubmitted or BookingStatus.UnderReview).ToList();

            var vRefunds = pendingRefunds
                .Where(r => bookingVenueMap.TryGetValue(r.BookingId, out var mappedId) && mappedId == vId)
                .ToList();

            venueCommissions.TryGetValue(vId, out var comm);
            var rows = confirmed.Select(b =>
            {
                var commission = CalculateEffectiveCommission(b.TotalPrice, b.PlatformCommission, b.StartUtc, b.EndUtc, comm, b.IsManualBooking, b.HasPriceSnapshot);
                var collected = Math.Min(commission, b.OnlinePaidAmount);
                return (b.TotalPrice, b.DepositAmount, Commission: commission, Online: b.OnlinePaidAmount, Collected: collected,
                    OwedToVenue: Math.Max(0m, b.OnlinePaidAmount - commission));
            }).ToList();

            var totalCommission = rows.Sum(r => r.Commission);
            var platformOwesVenue = rows.Sum(r => r.OwedToVenue);

            return new VenueFinancialSummaryItem(
                vId,
                venueNames.GetValueOrDefault(vId) ?? "ملعب غير معروف",
                vBookings.Count,
                confirmed.Count,
                cancelled.Count,
                pending.Count,
                TotalGrossAmount: rows.Sum(r => r.TotalPrice),
                TotalDepositAmount: rows.Sum(r => r.DepositAmount),
                TotalPlatformCommission: totalCommission,
                TotalVenueNetAmount: rows.Sum(r => r.TotalPrice - r.Commission),
                TotalDueAtVenueCash: rows.Sum(r => Math.Max(0m, r.TotalPrice - r.DepositAmount)),
                PendingRefundsCount: vRefunds.Count,
                PendingRefundsAmount: vRefunds.Sum(r => r.ExpectedRefundAmount),
                // Deposit minus commission, signed: negative when the commission is larger than the deposits. With the cash due at
                // the venue it always adds up to the venue's net.
                VenueDepositShare: rows.Sum(r => r.DepositAmount - r.Commission),
                Currency: "EGP",
                ManualBookings: confirmed.Count(b => b.IsManualBooking),
                PlatformCollectedOnline: rows.Sum(r => r.Online),
                CommissionCollected: rows.Sum(r => r.Collected),
                CommissionOutstanding: totalCommission - rows.Sum(r => r.Collected),
                PlatformOwesVenue: platformOwesVenue,
                CommissionType: comm?.CommissionType,
                CommissionRate: comm?.CommissionRate,
                CustomCommissionEnabled: comm?.CustomCommissionEnabled ?? false);
        })
        .OrderByDescending(s => s.TotalBookings)
        .ToList();

        return Result.Success<IReadOnlyList<VenueFinancialSummaryItem>>(summary);
    }

    /// <summary>Records that platform administration processed and sent the deposit refund to the player.</summary>
    public async Task<Result> ProcessRefundAsync(Guid refundId, string? notes, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var refund = await _dbContext.BookingRefunds.FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken);
        if (refund is null)
        {
            return Result.Failure(Error.NotFound("Refund.NotFound", "Refund record not found."));
        }

        var nowUtc = _clock.UtcNow;
        var actor = adminUserId.ToString("D");
        var result = refund.RecordManualRefund(
            refund.ExpectedRefundAmount,
            refund.ExpectedRefundAmount,
            notes?.Trim() ?? "تم رد العربون للاعب بواسطة إدارة المنصة",
            actor,
            nowUtc);

        if (result.IsFailure)
        {
            return result;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(new AuditEntry(
            "admin.booking.refund_processed",
            "BookingRefund",
            refundId.ToString("D"),
            adminUserId,
            AuditActorType.Admin,
            After: new { Status = nameof(RefundStatus.Refunded), refund.ExpectedRefundAmount, notes }), cancellationToken);

        return Result.Success();
    }

    private static decimal CalculateEffectiveCommission(
        decimal totalPrice,
        decimal storedCommission,
        DateTime startUtc,
        DateTime endUtc,
        VenueCommissionInfo? comm,
        bool isManualBooking = false,
        bool hasPriceSnapshot = false)
    {
        // A booking the venue entered itself (walk-in, phone) did not come through the platform: no commission.
        if (isManualBooking)
        {
            return 0m;
        }

        // The commission was fixed in the price quote the player agreed to (0 for an exempt venue); changing a venue's terms
        // later only affects new bookings. Only bookings made before quotes were snapshotted are worked out from today's terms.
        if (hasPriceSnapshot)
        {
            return storedCommission;
        }

        if (comm is { CustomCommissionEnabled: true })
        {
            if (comm.CommissionType == "Exempt")
            {
                return 0m;
            }

            if (comm.CommissionType == "Percentage")
            {
                var rate = comm.CommissionRate ?? 10m;
                return decimal.Round(totalPrice * rate / 100m, 2, MidpointRounding.AwayFromZero);
            }

            if (comm.CommissionType == "PerHourFixed")
            {
                var rate = comm.CommissionRate ?? 50m;
                var hours = (decimal)(endUtc - startUtc).TotalMinutes / 60m;
                return decimal.Round(hours * rate, 2, MidpointRounding.AwayFromZero);
            }
        }

        // If the booking already has a commission calculated from price quote, use it, or fallback to default 10%
        if (storedCommission > 0)
        {
            return storedCommission;
        }

        return decimal.Round(totalPrice * 10m / 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static AdminBookingListItem ToItem(
        Domain.Booking b,
        string? venueName = null,
        string? pitchName = null,
        string? customerName = null,
        string? customerPhone = null,
        VenueCommissionInfo? comm = null)
    {
        var commission = CalculateEffectiveCommission(b.TotalPrice.Amount, b.PlatformCommission.Amount, b.StartUtc, b.EndUtc, comm, b.IsManualBooking, b.PriceSnapshotJson != null);
        var venueNetAmount = Math.Max(0m, b.TotalPrice.Amount - commission);
        var venuePayAmount = Math.Max(0m, b.TotalPrice.Amount - b.DepositAmount.Amount);

        return new(b.Id, b.BookingReference, b.VenueId, b.PitchId, b.PlayerId, b.Status.ToString(), b.StartUtc, b.EndUtc,
            b.TotalPrice.Amount, b.DepositAmount.Amount, b.TotalPrice.Currency, b.IsManualBooking, b.CreatedAtUtc,
            commission, venueNetAmount, b.OnlinePaidAmount.Amount, venuePayAmount, b.IsCommissionPaid,
            venueName, pitchName, customerName ?? b.CustomerName, customerPhone ?? b.CustomerPhone);
    }
}
