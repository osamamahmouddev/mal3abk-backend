using Malabak.Modules.Subscriptions.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Subscriptions.Infrastructure;

/// <summary>
/// Plan enforcement (TASK-060). An owner's effective plan is their subscription's plan; owners without a subscription get
/// the plan an admin marked as default, and when no default exists nothing is limited (existing accounts keep working
/// until plans are set up). Expired, suspended and cancelled subscriptions allow no new resources.
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private readonly SubscriptionsDbContext _dbContext;

    public EntitlementService(SubscriptionsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> EnsureWithinLimitAsync(Guid ownerId, EntitlementResource resource, int currentCount, CancellationToken cancellationToken = default)
    {
        var (subscription, plan) = await EffectiveAsync(ownerId, cancellationToken);
        if (subscription is not null && !subscription.AllowsNewResources)
        {
            return Result.Failure(EntitlementErrors.SubscriptionInactive);
        }

        var limit = plan?.LimitFor(resource);
        return limit is { } max && currentCount >= max ? Result.Failure(EntitlementErrors.QuotaExceeded) : Result.Success();
    }

    public async Task<bool> HasFeatureAsync(Guid ownerId, string feature, CancellationToken cancellationToken = default)
    {
        var (subscription, plan) = await EffectiveAsync(ownerId, cancellationToken);
        if (plan is null)
        {
            return true; // no plans configured yet: nothing is gated
        }

        return (subscription is null || subscription.AllowsNewResources) && plan.Features.Contains(feature);
    }

    /// <summary>The owner's subscription (if any) and the plan whose limits apply.</summary>
    public async Task<(OwnerSubscription? Subscription, SubscriptionPlan? Plan)> EffectiveAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var subscription = await _dbContext.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.OwnerId == ownerId, cancellationToken);
        var plan = subscription is not null
            ? await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subscription.PlanId, cancellationToken)
            : await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.IsDefault && p.Status == PlanStatus.Active, cancellationToken);
        return (subscription, plan);
    }
}
