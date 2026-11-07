using System.Globalization;
using Malabak.Modules.Subscriptions.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Subscriptions.Infrastructure;

/// <summary>
/// Daily subscription sweep (TASK-708): sends expiry warnings 7 days and 1 day before the period ends, moves ended periods
/// into grace and finished grace periods to expired, and notifies the owner of each change. Safe to run more than once a
/// day: warnings are recorded and sent once per period, and transitions only happen when due.
/// </summary>
public sealed class SubscriptionLifecycleJob
{
    public const string RecurringJobId = "subscriptions-lifecycle";

    /// <summary>Daily at 06:00 UTC (08:00 Cairo in winter), so owners see warnings in the morning.</summary>
    public const string CronSchedule = "0 6 * * *";

    private readonly SubscriptionsDbContext _dbContext;
    private readonly INotificationPublisher _notifications;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;
    private readonly ILogger<SubscriptionLifecycleJob> _logger;

    public SubscriptionLifecycleJob(
        SubscriptionsDbContext dbContext, INotificationPublisher notifications, IAuditLog audit, IClock clock, ILogger<SubscriptionLifecycleJob> logger)
    {
        _dbContext = dbContext;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var horizon = now.AddDays(7);
        var due = await _dbContext.Subscriptions
            .Include(s => s.Events)
            .Where(s => ((s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Active) && s.CurrentPeriodEndUtc <= horizon)
                || (s.Status == SubscriptionStatus.Grace && s.GraceEndsAtUtc <= now))
            .ToListAsync(cancellationToken);
        var plans = await _dbContext.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);

        var changes = 0;
        foreach (var subscription in due)
        {
            var plan = plans[subscription.PlanId];
            var warning = subscription.DueWarning(now);
            if (warning is { } w)
            {
                subscription.MarkWarningSent(w, now);
                await NotifyAsync(subscription, NotificationTypes.SubscriptionExpiring, w.ToString(),
                    new() { ["planName"] = plan.Name, ["endDate"] = Date(subscription.CurrentPeriodEndUtc) }, cancellationToken);
                changes++;
            }

            var transition = subscription.Advance(plan.GraceDays, now);
            if (transition is SubscriptionEventType.EnteredGrace)
            {
                await NotifyAsync(subscription, NotificationTypes.SubscriptionGraceStarted, "grace", new() { ["graceEnd"] = Date(subscription.GraceEndsAtUtc!.Value) }, cancellationToken);
            }
            else if (transition is SubscriptionEventType.Expired)
            {
                await NotifyAsync(subscription, NotificationTypes.SubscriptionExpired, "expired", [], cancellationToken);
            }

            if (transition is not null)
            {
                await _audit.RecordAsync(new AuditEntry($"subscription.{transition.ToString()!.ToLowerInvariant()}", "Subscription", subscription.Id.ToString("D"),
                    null, AuditActorType.System, After: new { Status = subscription.Status.ToString() }), cancellationToken);
                changes++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (changes > 0)
        {
            _logger.LogInformation("Subscription sweep applied {Changes} changes", changes);
        }

        return changes;
    }

    private Task NotifyAsync(OwnerSubscription s, string type, string key, Dictionary<string, string> data, CancellationToken cancellationToken) =>
        _notifications.PublishAsync(new NotificationRequest(s.OwnerId, type, data, $"{type}:{s.Id:N}:{key}:{s.CurrentPeriodEndUtc:yyyyMMdd}"), cancellationToken);

    private static string Date(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
