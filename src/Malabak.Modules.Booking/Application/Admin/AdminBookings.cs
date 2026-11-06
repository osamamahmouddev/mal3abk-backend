using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Admin;

public sealed record AdminBookingSearch(
    string? Reference, Guid? VenueId, Guid? PlayerId, string? Status, DateTime? FromUtc, DateTime? ToUtc, int Page = 1, int PageSize = 25);

public sealed record AdminBookingListItem(
    Guid Id, string Reference, Guid VenueId, Guid PitchId, Guid PlayerId, string Status, DateTime StartUtc, DateTime EndUtc,
    decimal TotalPrice, decimal DepositAmount, string Currency, bool IsManual, DateTime CreatedAtUtc);

public sealed record AdminBookingPage(IReadOnlyList<AdminBookingListItem> Items, int Page, int PageSize, int Total);

public sealed record AdminBookingHistoryItem(string FromStatus, string ToStatus, string? Reason, string ChangedBy, DateTime ChangedAtUtc);

/// <summary>Proof metadata only: the file itself stays behind the reviewed, short-lived download flow.</summary>
public sealed record AdminProofItem(
    Guid Id, string ContentType, long FileSizeBytes, DateTime UploadedAtUtc, bool IsScanClean, bool? IsApproved, string? RejectionReason,
    DateTime? ReviewedAtUtc, string? ReviewedBy, bool IsArchived);

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
    IReadOnlyList<AdminRefundItem> Refunds);

public static class AdminBookingErrors
{
    public static readonly Error InvalidFilter = Error.Validation("AdminBookings.InvalidFilter", "Invalid booking search filter.");
}

/// <summary>
/// Cross-venue booking monitoring for platform staff (TASK-703): search, full detail with status, payment, proof and refund
/// history, and an audited force-cancel that follows the venue-cancellation rules (full deposit refund owed to the player).
/// </summary>
public sealed class AdminBookingService
{
    public const int MaxPageSize = 100;
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);

    private readonly IBookingDbContext _dbContext;
    private readonly ICommandHandler<VenueCancelBookingCommand, Result> _cancel;
    private readonly IAuditLog _audit;

    public AdminBookingService(IBookingDbContext dbContext, ICommandHandler<VenueCancelBookingCommand, Result> cancel, IAuditLog audit)
    {
        _dbContext = dbContext;
        _cancel = cancel;
        _audit = audit;
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

        if (search.VenueId is { } venueId)
        {
            query = query.Where(b => b.VenueId == venueId);
        }

        if (search.PlayerId is { } playerId)
        {
            query = query.Where(b => b.PlayerId == playerId);
        }

        if (status is { } s)
        {
            query = query.Where(b => b.Status == s);
        }

        if (search.FromUtc is { } fromUtc)
        {
            query = query.Where(b => b.StartUtc >= fromUtc);
        }

        if (search.ToUtc is { } toUtc)
        {
            query = query.Where(b => b.StartUtc < toUtc);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(b => b.StartUtc)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .ToListAsync(cancellationToken);
        return Result.Success(new AdminBookingPage(rows.Select(ToItem).ToList(), search.Page, search.PageSize, total));
    }

    public async Task<Result<AdminBookingDetail>> GetAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId && !b.IsDeleted, cancellationToken);
        if (booking is null)
        {
            return Result.Failure<AdminBookingDetail>(BookingErrors.NotFound);
        }

        var history = await _dbContext.StatusHistories.AsNoTracking().Where(h => h.BookingId == bookingId)
            .OrderBy(h => h.ChangedAtUtc).ToListAsync(cancellationToken);
        var proofs = await _dbContext.PaymentProofs.AsNoTracking().Where(p => p.BookingId == bookingId)
            .OrderBy(p => p.UploadedAtUtc).ToListAsync(cancellationToken);
        var refunds = await _dbContext.BookingRefunds.AsNoTracking().Where(r => r.BookingId == bookingId)
            .OrderBy(r => r.CreatedAtUtc).ToListAsync(cancellationToken);

        return Result.Success(new AdminBookingDetail(
            ToItem(booking), booking.CancellationReason, booking.RejectionReason, booking.HoldExpiresAtUtc, booking.ConfirmedAtUtc, booking.CancelledAtUtc,
            booking.CustomerName, booking.Notes, booking.PolicySnapshot,
            history.Select(h => new AdminBookingHistoryItem(h.FromStatus.ToString(), h.ToStatus.ToString(), h.Reason, h.ChangedByUserId, h.ChangedAtUtc)).ToList(),
            proofs.Select(p => new AdminProofItem(p.Id, p.ContentType, p.FileSizeBytes, p.UploadedAtUtc, p.IsScanClean, p.IsApproved, p.RejectionReason,
                p.ReviewedAtUtc, p.ReviewedByUserId, p.IsArchived)).ToList(),
            refunds.Select(r => new AdminRefundItem(r.Id, r.Status.ToString(), r.ExpectedRefundAmount, r.ActualRefundAmount, r.Currency, r.Reason, r.RecordedAtUtc)).ToList()));
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

    private static AdminBookingListItem ToItem(Domain.Booking b) =>
        new(b.Id, b.BookingReference, b.VenueId, b.PitchId, b.PlayerId, b.Status.ToString(), b.StartUtc, b.EndUtc,
            b.TotalPrice.Amount, b.DepositAmount.Amount, b.TotalPrice.Currency, b.IsManualBooking, b.CreatedAtUtc);
}
