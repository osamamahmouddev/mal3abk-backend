using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Admin;

/// <summary>
/// Venue figures for the admin dashboard (TASK-710): <c>venues.active</c> (approved, published, not deleted, now),
/// <c>venues.pending_approval</c> (waiting for a decision now), <c>venues.suspended</c>, <c>pitches.active</c> (active pitches
/// of active venues) and <c>venues.new</c> (venues created in the period).
/// </summary>
public sealed class VenueMetricsProvider : IAdminMetricsProvider
{
    public const int TopCities = 10;

    private readonly VenuesDbContext _dbContext;

    public VenueMetricsProvider(VenuesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminMetricsPart> GetAsync(MetricsPeriod period, CancellationToken cancellationToken = default)
    {
        var venues = _dbContext.Venues.AsNoTracking().Where(v => !v.IsDeleted);
        var active = venues.Where(v => v.ApprovalStatus == VenueApprovalStatus.Approved && v.IsPublished);
        var activeIds = active.Select(v => v.Id);

        var activeCount = await active.CountAsync(cancellationToken);
        var pending = await venues.CountAsync(v => v.ApprovalStatus == VenueApprovalStatus.Submitted, cancellationToken);
        var suspended = await venues.CountAsync(v => v.ApprovalStatus == VenueApprovalStatus.Suspended, cancellationToken);
        var pitches = await _dbContext.Pitches.AsNoTracking().CountAsync(p => !p.IsDeleted && p.IsActive && activeIds.Contains(p.VenueId), cancellationToken);
        var created = await venues.Where(v => v.CreatedAtUtc >= period.FromUtc && v.CreatedAtUtc < period.ToUtc).Select(v => v.CreatedAtUtc).ToListAsync(cancellationToken);
        var byCity = await active.GroupBy(v => v.City).Select(g => new { City = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).Take(TopCities).ToListAsync(cancellationToken);

        return new AdminMetricsPart(
            new Dictionary<string, decimal>
            {
                ["venues.active"] = activeCount,
                ["venues.pending_approval"] = pending,
                ["venues.suspended"] = suspended,
                ["venues.new"] = created.Count,
                ["pitches.active"] = pitches
            },
            new Dictionary<string, IReadOnlyList<SeriesPoint>> { ["venues.new"] = period.Daily(created) },
            new Dictionary<string, IReadOnlyDictionary<string, int>> { ["venues.active_by_city"] = byCity.ToDictionary(c => c.City, c => c.Count) });
    }
}
