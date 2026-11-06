using Malabak.SharedKernel;

namespace Malabak.Modules.Subscriptions.Domain;

public enum PlanStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}

/// <summary>Plan terms as entered by an admin. A limit of null means unlimited.</summary>
public sealed record PlanTerms(
    string Code,
    string Name,
    string? Description,
    decimal MonthlyPrice,
    decimal AnnualPrice,
    int? MaxVenues,
    int? MaxPitches,
    int? MaxStaffPerVenue,
    IReadOnlyList<string> Features,
    int TrialDays,
    int GraceDays,
    bool IsPublic,
    int SortOrder,
    string Currency = "EGP");

/// <summary>A SaaS plan for venue owners (TASK-707). Archived plans keep existing subscribers but cannot be assigned.</summary>
public sealed class SubscriptionPlan
{
    public const int MaxFeatures = 20;

    private List<string> _features = [];

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal MonthlyPrice { get; private set; }
    public decimal AnnualPrice { get; private set; }
    public string Currency { get; private set; } = "EGP";
    public int? MaxVenues { get; private set; }
    public int? MaxPitches { get; private set; }
    public int? MaxStaffPerVenue { get; private set; }
    public IReadOnlyList<string> Features => _features;
    public int TrialDays { get; private set; }
    public int GraceDays { get; private set; }
    public bool IsPublic { get; private set; }
    public bool IsDefault { get; private set; }
    public int SortOrder { get; private set; }
    public PlanStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private SubscriptionPlan()
    {
    }

    public static Result<SubscriptionPlan> Create(PlanTerms terms, DateTime nowUtc)
    {
        var plan = new SubscriptionPlan { Id = IdGenerator.NewGuid(), Status = PlanStatus.Draft, CreatedAtUtc = nowUtc };
        var applied = plan.Apply(terms, nowUtc);
        return applied.IsSuccess ? Result.Success(plan) : Result.Failure<SubscriptionPlan>(applied.Error);
    }

    public Result Update(PlanTerms terms, DateTime nowUtc) => Apply(terms, nowUtc);

    public Result SetStatus(PlanStatus status, DateTime nowUtc)
    {
        if (!Enum.IsDefined(status))
        {
            return Result.Failure(PlanErrors.Invalid);
        }

        if (status != PlanStatus.Active && IsDefault)
        {
            return Result.Failure(PlanErrors.DefaultMustBeActive);
        }

        Status = status;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public void SetDefault(bool isDefault, DateTime nowUtc)
    {
        IsDefault = isDefault;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Price for one billing period.</summary>
    public decimal PriceFor(BillingCycle cycle) => cycle == BillingCycle.Annual ? AnnualPrice : MonthlyPrice;

    public int? LimitFor(SharedKernel.Contracts.EntitlementResource resource) => resource switch
    {
        SharedKernel.Contracts.EntitlementResource.Venues => MaxVenues,
        SharedKernel.Contracts.EntitlementResource.Pitches => MaxPitches,
        _ => MaxStaffPerVenue
    };

    private Result Apply(PlanTerms t, DateTime nowUtc)
    {
        var code = t.Code?.Trim().ToLowerInvariant() ?? string.Empty;
        var features = (t.Features ?? []).Select(f => f.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (code.Length is < 2 or > 40 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            || string.IsNullOrWhiteSpace(t.Name) || t.Name.Trim().Length > 100 || t.Description?.Length > 1000
            || t.MonthlyPrice < 0 || t.AnnualPrice < 0 || t.MonthlyPrice > 1_000_000 || t.AnnualPrice > 10_000_000
            || t.MaxVenues is < 1 || t.MaxPitches is < 1 || t.MaxStaffPerVenue is < 0
            || t.TrialDays is < 0 or > 90 || t.GraceDays is < 0 or > 60 || features.Count > MaxFeatures || features.Exists(f => f.Length > 60)
            || t.Currency?.Length != 3)
        {
            return Result.Failure(PlanErrors.Invalid);
        }

        Code = code;
        Name = t.Name.Trim();
        Description = string.IsNullOrWhiteSpace(t.Description) ? null : t.Description.Trim();
        MonthlyPrice = decimal.Round(t.MonthlyPrice, 2);
        AnnualPrice = decimal.Round(t.AnnualPrice, 2);
        Currency = t.Currency.ToUpperInvariant();
        MaxVenues = t.MaxVenues;
        MaxPitches = t.MaxPitches;
        MaxStaffPerVenue = t.MaxStaffPerVenue;
        _features = features;
        TrialDays = t.TrialDays;
        GraceDays = t.GraceDays;
        IsPublic = t.IsPublic;
        SortOrder = t.SortOrder;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}

public static class PlanErrors
{
    public static readonly Error Invalid = new("Plan.Invalid",
        "Check the plan: code 2-40 letters/digits/-/_, name up to 100 characters, prices not negative, limits at least 1 (staff 0+), trial up to 90 days, grace up to 60, at most 20 features.");
    public static readonly Error NotFound = new("Plan.NotFound", "Plan not found.");
    public static readonly Error DuplicateCode = new("Plan.DuplicateCode", "Another plan already uses this code.");
    public static readonly Error NotAssignable = new("Plan.NotAssignable", "Only active plans can be assigned.");
    public static readonly Error DefaultMustBeActive = new("Plan.DefaultMustBeActive", "The default plan must stay active. Choose another default first.");
}
