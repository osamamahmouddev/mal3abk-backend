using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Admin;

/// <summary>
/// Booking figures for the admin dashboard (TASK-710).
/// <list type="bullet">
/// <item><c>bookings.created</c>: bookings placed in the period (creation time; abandoned drafts excluded, manual bookings included).</item>
/// <item><c>bookings.confirmed</c>: bookings confirmed in the period (confirmation time).</item>
/// <item><c>bookings.confirmed_value</c>: total price of those confirmed bookings, in EGP. This is venue revenue, not platform
/// revenue: Malabak does not collect booking money.</item>
/// <item><c>disputes.open</c>: payment disputes not yet resolved, now.</item>
/// </list>
/// </summary>
public sealed class BookingMetricsProvider : IAdminMetricsProvider
{
    private readonly IBookingDbContext _dbContext;

    public BookingMetricsProvider(IBookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminMetricsPart> GetAsync(MetricsPeriod period, CancellationToken cancellationToken = default)
    {
        var created = await _dbContext.Bookings.AsNoTracking()
            .Where(b => !b.IsDeleted && b.Status != BookingStatus.Draft && b.CreatedAtUtc >= period.FromUtc && b.CreatedAtUtc < period.ToUtc)
            .Select(b => new { b.CreatedAtUtc, b.Status })
            .ToListAsync(cancellationToken);
        var confirmed = await _dbContext.Bookings.AsNoTracking()
            .Where(b => !b.IsDeleted && b.ConfirmedAtUtc >= period.FromUtc && b.ConfirmedAtUtc < period.ToUtc)
            .Select(b => new { ConfirmedAtUtc = b.ConfirmedAtUtc!.Value, Value = b.TotalPrice.Amount })
            .ToListAsync(cancellationToken);
        var disputes = await _dbContext.PaymentDisputes.AsNoTracking().CountAsync(d => d.Status != DisputeStatus.Resolved, cancellationToken);

        return new AdminMetricsPart(
            new Dictionary<string, decimal>
            {
                ["bookings.created"] = created.Count,
                ["bookings.confirmed"] = confirmed.Count,
                ["bookings.confirmed_value"] = confirmed.Sum(c => c.Value),
                ["disputes.open"] = disputes
            },
            new Dictionary<string, IReadOnlyList<SeriesPoint>>
            {
                ["bookings.created"] = period.Daily(created.Select(c => c.CreatedAtUtc)),
                ["bookings.confirmed"] = period.Daily(confirmed.Select(c => c.ConfirmedAtUtc))
            },
            new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["bookings.by_status"] = created.GroupBy(c => c.Status.ToString()).ToDictionary(g => g.Key, g => g.Count())
            });
    }
}
