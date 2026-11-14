namespace Malabak.SharedKernel.Contracts;

/// <summary>Resources limited by a venue owner's subscription plan.</summary>
public enum EntitlementResource
{
    Venues = 0,
    Pitches = 1,
    StaffPerVenue = 2
}

/// <summary>Plan features beyond plain quotas.</summary>
public static class PlanFeatures
{
    public const string AdvancedReports = "reports.advanced";
    public const string CsvExport = "reports.csv_export";
    public const string MultipleVenues = "venues.multiple";
    public const string PrioritySupport = "support.priority";
}

public static class EntitlementErrors
{
    public static readonly Error QuotaExceeded = new("Subscription.QuotaExceeded", "Your plan's limit is reached. Upgrade the plan to add more.");
    public static readonly Error SubscriptionInactive = new("Subscription.Inactive", "Your subscription has expired or is suspended. Renew it to add new venues, pitches or staff; existing bookings keep working.");
}

/// <summary>
/// Server-side plan enforcement (TASK-060). Callers pass the current usage they already know (the subscription module
/// never reads other modules' data); creation is refused when the plan's limit is reached or the subscription is not active.
/// </summary>
public interface IEntitlementService
{
    Task<Result> EnsureWithinLimitAsync(Guid ownerId, EntitlementResource resource, int currentCount, CancellationToken cancellationToken = default);

    Task<bool> HasFeatureAsync(Guid ownerId, string feature, CancellationToken cancellationToken = default);
}

public sealed record OwnerSubscriptionSummary(
    Guid OwnerId,
    string PlanCode,
    string PlanName,
    string Cycle,
    string Status,
    DateTime? CurrentPeriodEndUtc);

public interface IOwnerSubscriptionReader
{
    Task<IReadOnlyDictionary<Guid, OwnerSubscriptionSummary>> GetSummariesBatchAsync(
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, OwnerSubscriptionSummary>>(new Dictionary<Guid, OwnerSubscriptionSummary>());
}
