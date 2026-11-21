using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Application.Admin;

/// <summary>What the platform owes one venue: its share of the deposits the platform received, less what was already sent.</summary>
public sealed record VenuePayoutBalance(
    Guid VenueId,
    string VenueName,
    int SettledBookings,
    decimal DepositsReceived,
    decimal Commission,
    decimal RefundedToPlayers,
    decimal VenueShare,
    decimal PaidOut,
    decimal Balance,
    DateTime? LastPayoutAtUtc,
    string Currency = "EGP");

public sealed record VenuePayoutItem(
    Guid Id,
    Guid VenueId,
    decimal Amount,
    string Currency,
    string Method,
    string Reference,
    string? Notes,
    DateTime PaidAtUtc,
    DateTime RecordedAtUtc);

/// <summary>One booking whose deposit the platform holds, with what it means for the venue.</summary>
public sealed record VenuePayoutBookingLine(
    Guid BookingId,
    string Reference,
    string Status,
    DateTime StartUtc,
    decimal TotalPrice,
    decimal DepositReceived,
    decimal Commission,
    decimal Refunded,
    decimal VenueShare);

public sealed record VenuePayoutStatement(
    VenuePayoutBalance Balance,
    IReadOnlyList<VenuePayoutItem> Payouts,
    IReadOnlyList<VenuePayoutBookingLine> Bookings);

public sealed record RecordVenuePayoutInput(decimal Amount, string? Method, string? Reference, string? Notes, DateTime? PaidAtUtc);

/// <summary>
/// Settlement with venues under the commission model. Players pay the deposit to the platform; once the platform team confirms
/// it, the platform keeps its commission (fixed in the booking's price quote) and owes the venue the rest. A booking that ends
/// cancelled or as a no-show keeps only what was not refunded to the player, and the commission still comes out of that first.
/// Manual bookings never pass through the platform, so they are not part of any settlement.
/// </summary>
public sealed class VenuePayoutService
{
    public const int StatementBookingLimit = 200;

    private static readonly BookingStatus[] _settledStatuses =
        [BookingStatus.Confirmed, BookingStatus.Completed, BookingStatus.Cancelled, BookingStatus.NoShow];

    private readonly IBookingDbContext _dbContext;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;
    private readonly IVenueDirectory _venues;
    private readonly INotificationPublisher? _notifications;
    private readonly ILogger<VenuePayoutService>? _logger;

    public VenuePayoutService(
        IBookingDbContext dbContext,
        IAuditLog audit,
        IClock clock,
        IVenueDirectory venues,
        INotificationPublisher? notifications = null,
        ILogger<VenuePayoutService>? logger = null)
    {
        _dbContext = dbContext;
        _audit = audit;
        _clock = clock;
        _venues = venues;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>Every venue the platform holds money for or has paid, the ones it owes the most first.</summary>
    public async Task<IReadOnlyList<VenuePayoutBalance>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        var lines = await LoadLinesAsync(venueId: null, cancellationToken);
        var payouts = await _dbContext.VenuePayouts.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .GroupBy(p => p.VenueId)
            .Select(g => new { VenueId = g.Key, Paid = g.Sum(p => p.Amount), Last = g.Max(p => p.PaidAtUtc) })
            .ToListAsync(cancellationToken);

        var venueIds = lines.Select(l => l.VenueId).Concat(payouts.Select(p => p.VenueId)).Distinct().ToList();
        var names = await _venues.GetVenueNamesAsync(venueIds, cancellationToken);

        return venueIds
            .Select(id =>
            {
                var paid = payouts.FirstOrDefault(p => p.VenueId == id);
                return ToBalance(id, names.GetValueOrDefault(id) ?? string.Empty, lines.Where(l => l.VenueId == id).ToList(),
                    paid?.Paid ?? 0m, paid?.Last);
            })
            .OrderByDescending(b => b.Balance)
            .ThenBy(b => b.VenueName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The venue's balance, every payout, and the latest bookings behind the balance.</summary>
    public async Task<Result<VenuePayoutStatement>> GetStatementAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        var names = await _venues.GetVenueNamesAsync([venueId], cancellationToken);
        if (!names.TryGetValue(venueId, out var venueName))
        {
            return Result.Failure<VenuePayoutStatement>(VenuePayoutErrors.VenueNotFound);
        }

        var lines = await LoadLinesAsync(venueId, cancellationToken);
        var payouts = await _dbContext.VenuePayouts.AsNoTracking()
            .Where(p => p.VenueId == venueId && !p.IsDeleted)
            .OrderByDescending(p => p.PaidAtUtc)
            .ToListAsync(cancellationToken);

        var balance = ToBalance(venueId, venueName, lines, payouts.Sum(p => p.Amount), payouts.FirstOrDefault()?.PaidAtUtc);
        var bookings = lines
            .OrderByDescending(l => l.StartUtc)
            .Take(StatementBookingLimit)
            .Select(l => new VenuePayoutBookingLine(l.BookingId, l.Reference, l.Status.ToString(), l.StartUtc, l.TotalPrice,
                l.DepositReceived, l.Commission, l.Refunded, l.VenueShare))
            .ToList();

        return Result.Success(new VenuePayoutStatement(balance, payouts.Select(ToItem).ToList(), bookings));
    }

    /// <summary>
    /// Records a transfer the platform made to the venue. It can never exceed what the platform owes it, so the books cannot
    /// show the venue being paid money it did not earn.
    /// </summary>
    public async Task<Result<VenuePayoutItem>> RecordAsync(
        Guid venueId, RecordVenuePayoutInput input, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var names = await _venues.GetVenueNamesAsync([venueId], cancellationToken);
        if (!names.TryGetValue(venueId, out var venueName))
        {
            return Result.Failure<VenuePayoutItem>(VenuePayoutErrors.VenueNotFound);
        }

        var now = _clock.UtcNow;
        var created = VenuePayout.Record(Guid.NewGuid(), venueId, input.Amount, "EGP", input.Method, input.Reference, input.Notes,
            input.PaidAtUtc ?? now, adminUserId, now);
        if (created.IsFailure)
        {
            return Result.Failure<VenuePayoutItem>(created.Error);
        }

        // One payout per venue at a time: two admins recording at once must not both pass the balance check. The lock is held
        // until the transaction ends.
        var relational = _dbContext.Database.IsRelational();
        await using var transaction = relational ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        if (relational)
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"venue-payout:" + venueId.ToString("D")}, 0))", cancellationToken);
        }

        var lines = await LoadLinesAsync(venueId, cancellationToken);
        var paid = await _dbContext.VenuePayouts.Where(p => p.VenueId == venueId && !p.IsDeleted).SumAsync(p => p.Amount, cancellationToken);
        var balance = lines.Sum(l => l.VenueShare) - paid;
        if (created.Value.Amount > balance)
        {
            return Result.Failure<VenuePayoutItem>(VenuePayoutErrors.ExceedsBalance);
        }

        _dbContext.VenuePayouts.Add(created.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var payout = created.Value;
        await _audit.RecordAsync(new AuditEntry(
            "admin.venue.payout_recorded",
            "VenuePayout",
            payout.Id.ToString("D"),
            adminUserId,
            AuditActorType.Admin,
            Before: new { Balance = balance },
            After: new { payout.VenueId, payout.Amount, payout.Method, payout.PaidAtUtc, Balance = balance - payout.Amount }), cancellationToken);

        await NotifyVenueAsync(payout, venueName, cancellationToken);
        return Result.Success(ToItem(payout));
    }

    private async Task NotifyVenueAsync(VenuePayout payout, string venueName, CancellationToken cancellationToken)
    {
        if (_notifications is null)
        {
            return;
        }

        try
        {
            var recipients = await _venues.GetStaffUserIdsAsync(payout.VenueId, Permissions.ReportsView, cancellationToken);
            var data = new Dictionary<string, string>
            {
                ["venueId"] = payout.VenueId.ToString("D"),
                ["venueName"] = venueName,
                ["amount"] = payout.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                ["reference"] = payout.Reference,
            };
            foreach (var userId in recipients)
            {
                await _notifications.PublishAsync(
                    new NotificationRequest(userId, NotificationTypes.VenuePayoutRecorded, data, $"payout:{payout.Id:D}:{userId:D}"),
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The payout is recorded either way; the notice is a courtesy.
            _logger?.LogError(ex, "Failed to notify venue {VenueId} of payout {PayoutId}", payout.VenueId, payout.Id);
        }
    }

    private async Task<List<Line>> LoadLinesAsync(Guid? venueId, CancellationToken cancellationToken)
    {
        var query = _dbContext.Bookings.AsNoTracking()
            .Where(b => !b.IsDeleted && !b.IsManualBooking && b.OnlinePaidAmount.Amount > 0 && _settledStatuses.Contains(b.Status));
        if (venueId.HasValue)
        {
            query = query.Where(b => b.VenueId == venueId.Value);
        }

        var rows = await query
            .Select(b => new
            {
                b.Id,
                b.VenueId,
                b.BookingReference,
                b.Status,
                b.StartUtc,
                Total = b.TotalPrice.Amount,
                Received = b.OnlinePaidAmount.Amount,
                Commission = b.PlatformCommission.Amount,
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var refunds = await _dbContext.BookingRefunds.AsNoTracking()
            .Where(r => ids.Contains(r.BookingId) && (r.Status == RefundStatus.Pending || r.Status == RefundStatus.Refunded))
            .Select(r => new { r.BookingId, Amount = r.Status == RefundStatus.Refunded ? r.ActualRefundAmount ?? r.ExpectedRefundAmount : r.ExpectedRefundAmount })
            .ToDictionaryAsync(r => r.BookingId, r => r.Amount, cancellationToken);

        return rows.Select(r =>
        {
            // A pending refund is counted as already gone: it is owed to the player and must not be paid to the venue meanwhile.
            var refunded = Math.Min(r.Received, refunds.GetValueOrDefault(r.Id));
            var kept = r.Received - refunded;
            var commission = Math.Min(r.Commission, kept);
            return new Line(r.Id, r.VenueId, r.BookingReference, r.Status, r.StartUtc, r.Total, r.Received, commission, refunded,
                kept - commission);
        }).ToList();
    }

    private static VenuePayoutBalance ToBalance(Guid venueId, string venueName, List<Line> lines, decimal paid, DateTime? lastPayout)
    {
        var share = lines.Sum(l => l.VenueShare);
        return new VenuePayoutBalance(venueId, venueName, lines.Count, lines.Sum(l => l.DepositReceived), lines.Sum(l => l.Commission),
            lines.Sum(l => l.Refunded), share, paid, share - paid, lastPayout);
    }

    private static VenuePayoutItem ToItem(VenuePayout p) =>
        new(p.Id, p.VenueId, p.Amount, p.Currency, p.Method, p.Reference, p.Notes, p.PaidAtUtc, p.CreatedAtUtc);

    private sealed record Line(
        Guid BookingId,
        Guid VenueId,
        string Reference,
        BookingStatus Status,
        DateTime StartUtc,
        decimal TotalPrice,
        decimal DepositReceived,
        decimal Commission,
        decimal Refunded,
        decimal VenueShare);
}
