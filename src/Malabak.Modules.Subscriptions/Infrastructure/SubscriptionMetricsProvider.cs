using Malabak.Modules.Subscriptions.Domain;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Subscriptions.Infrastructure;

/// <summary>
/// Subscription figures for the admin dashboard (TASK-710).
/// <list type="bullet">
/// <item><c>subscriptions.active</c>: owners whose subscription is Active or in Grace now (paying customers).</item>
/// <item><c>subscriptions.trial</c>: owners in a trial now (not paying yet, not in MRR).</item>
/// <item><c>mrr</c>: monthly recurring revenue now, in EGP: the plan's monthly price for monthly subscriptions and the annual
/// price divided by 12 for annual ones, over Active and Grace subscriptions.</item>
/// <item><c>revenue.collected</c>: money recorded as received in the period (payment date), excluding payments whose invoice
/// was voided. This is cash collected, not MRR.</item>
/// </list>
/// </summary>
public sealed class SubscriptionMetricsProvider : IAdminMetricsProvider
{
    private readonly SubscriptionsDbContext _dbContext;

    public SubscriptionMetricsProvider(SubscriptionsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminMetricsPart> GetAsync(MetricsPeriod period, CancellationToken cancellationToken = default)
    {
        var plans = await _dbContext.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        var current = await _dbContext.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Grace || s.Status == SubscriptionStatus.Trial)
            .Select(s => new { s.PlanId, s.Cycle, s.Status })
            .ToListAsync(cancellationToken);
        var paying = current.Where(s => s.Status != SubscriptionStatus.Trial).ToList();

        var mrr = 0m;
        foreach (var subscription in paying)
        {
            if (plans.TryGetValue(subscription.PlanId, out var plan))
            {
                mrr += subscription.Cycle == BillingCycle.Annual ? Math.Round(plan.AnnualPrice / 12m, 2) : plan.MonthlyPrice;
            }
        }

        var voided = _dbContext.Invoices.Where(i => i.Status == InvoiceStatus.Void).Select(i => i.PaymentId);
        var payments = await _dbContext.Payments.AsNoTracking()
            .Where(p => p.PaidAtUtc >= period.FromUtc && p.PaidAtUtc < period.ToUtc && !voided.Contains(p.Id))
            .Select(p => new { p.PaidAtUtc, p.Amount })
            .ToListAsync(cancellationToken);
        var amounts = payments.GroupBy(p => p.PaidAtUtc).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        return new AdminMetricsPart(
            new Dictionary<string, decimal>
            {
                ["subscriptions.active"] = paying.Count,
                ["subscriptions.trial"] = current.Count - paying.Count,
                ["mrr"] = mrr,
                ["revenue.collected"] = payments.Sum(p => p.Amount)
            },
            new Dictionary<string, IReadOnlyList<SeriesPoint>>
            {
                ["revenue.collected"] = period.Daily(amounts.Keys, at => amounts[at])
            },
            new Dictionary<string, IReadOnlyDictionary<string, int>>
            {
                ["subscriptions.by_plan"] = paying.GroupBy(s => plans.TryGetValue(s.PlanId, out var plan) ? plan.Code : "unknown")
                    .ToDictionary(g => g.Key, g => g.Count())
            });
    }
}
