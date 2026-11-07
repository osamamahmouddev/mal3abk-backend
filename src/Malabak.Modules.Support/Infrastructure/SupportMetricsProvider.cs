using Malabak.Modules.Support.Domain;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Support.Infrastructure;

/// <summary><c>support.open</c>: tickets not resolved or closed now; <c>support.opened</c>: tickets opened in the period.</summary>
public sealed class SupportMetricsProvider : IAdminMetricsProvider
{
    private readonly SupportDbContext _dbContext;

    public SupportMetricsProvider(SupportDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminMetricsPart> GetAsync(MetricsPeriod period, CancellationToken cancellationToken = default)
    {
        var open = await _dbContext.Tickets.AsNoTracking()
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .GroupBy(t => t.Category).Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var opened = await _dbContext.Tickets.AsNoTracking().CountAsync(t => t.CreatedAtUtc >= period.FromUtc && t.CreatedAtUtc < period.ToUtc, cancellationToken);

        return new AdminMetricsPart(
            new Dictionary<string, decimal> { ["support.open"] = open.Sum(o => o.Count), ["support.opened"] = opened },
            new Dictionary<string, IReadOnlyList<SeriesPoint>>(),
            new Dictionary<string, IReadOnlyDictionary<string, int>> { ["support.open_by_category"] = open.ToDictionary(o => o.Category.ToString(), o => o.Count) });
    }
}
