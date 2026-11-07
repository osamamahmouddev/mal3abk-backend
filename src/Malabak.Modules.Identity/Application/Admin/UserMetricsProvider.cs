using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Application.Admin;

/// <summary>
/// User figures for the admin dashboard (TASK-710): <c>users.total</c> (accounts not deleted, now), <c>users.new</c> (sign-ups
/// in the period), <c>users.suspended</c> (now) and the number of accounts holding each role.
/// </summary>
public sealed class UserMetricsProvider : IAdminMetricsProvider
{
    private readonly IdentityDbContext _dbContext;

    public UserMetricsProvider(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminMetricsPart> GetAsync(MetricsPeriod period, CancellationToken cancellationToken = default)
    {
        var users = _dbContext.Users.AsNoTracking().Where(u => !u.IsDeleted);
        var total = await users.CountAsync(cancellationToken);
        var suspended = await users.CountAsync(u => u.SuspendedAtUtc != null, cancellationToken);
        var created = await users.Where(u => u.CreatedAtUtc >= period.FromUtc && u.CreatedAtUtc < period.ToUtc).Select(u => u.CreatedAtUtc).ToListAsync(cancellationToken);

        var byRole = new Dictionary<string, int>();
        foreach (var role in UserRole.All)
        {
            byRole[role] = await UserQueries.WithRole(_dbContext, role).AsNoTracking().CountAsync(u => !u.IsDeleted, cancellationToken);
        }

        return new AdminMetricsPart(
            new Dictionary<string, decimal> { ["users.total"] = total, ["users.new"] = created.Count, ["users.suspended"] = suspended },
            new Dictionary<string, IReadOnlyList<SeriesPoint>> { ["users.new"] = period.Daily(created) },
            new Dictionary<string, IReadOnlyDictionary<string, int>> { ["users.by_role"] = byRole });
    }
}
