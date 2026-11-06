using Malabak.Modules.Subscriptions.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Subscriptions.Infrastructure;

public sealed record PlanDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    decimal MonthlyPrice,
    decimal AnnualPrice,
    string Currency,
    int? MaxVenues,
    int? MaxPitches,
    int? MaxStaffPerVenue,
    IReadOnlyList<string> Features,
    int TrialDays,
    int GraceDays,
    bool IsPublic,
    bool IsDefault,
    int SortOrder,
    string Status,
    int Subscribers,
    DateTime UpdatedAtUtc);

/// <summary>Plan management (TASK-707). Every change is audited; codes are unique; the default plan must stay active.</summary>
public sealed class PlanService
{
    private readonly SubscriptionsDbContext _dbContext;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public PlanService(SubscriptionsDbContext dbContext, IAuditLog audit, IClock clock)
    {
        _dbContext = dbContext;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>Plans shown to venue owners: active and public, cheapest first.</summary>
    public async Task<IReadOnlyList<PlanDto>> ListPublicAsync(CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken)).Where(p => p.IsPublic && p.Status == nameof(PlanStatus.Active)).ToList();

    public async Task<IReadOnlyList<PlanDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _dbContext.Plans.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.MonthlyPrice).ToListAsync(cancellationToken);
        var counts = await _dbContext.Subscriptions.AsNoTracking()
            .Where(s => s.Status != SubscriptionStatus.Cancelled)
            .GroupBy(s => s.PlanId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return plans.Select(p => ToDto(p, counts.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<Result<PlanDto>> CreateAsync(PlanTerms terms, Guid adminId, CancellationToken cancellationToken = default)
    {
        var created = SubscriptionPlan.Create(terms, _clock.UtcNow);
        if (created.IsFailure)
        {
            return Result.Failure<PlanDto>(created.Error);
        }

        if (await _dbContext.Plans.AnyAsync(p => p.Code == created.Value.Code, cancellationToken))
        {
            return Result.Failure<PlanDto>(PlanErrors.DuplicateCode);
        }

        _dbContext.Plans.Add(created.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var dto = ToDto(created.Value, 0);
        await _audit.RecordAsync(new AuditEntry("subscription.plan_created", "SubscriptionPlan", dto.Id.ToString("D"), adminId, AuditActorType.Admin, After: dto), cancellationToken);
        return Result.Success(dto);
    }

    public async Task<Result<PlanDto>> UpdateAsync(Guid planId, PlanTerms terms, Guid adminId, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PlanDto>(PlanErrors.NotFound);
        }

        var before = ToDto(plan, 0);
        var updated = plan.Update(terms, _clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result.Failure<PlanDto>(updated.Error);
        }

        if (await _dbContext.Plans.AnyAsync(p => p.Code == plan.Code && p.Id != plan.Id, cancellationToken))
        {
            return Result.Failure<PlanDto>(PlanErrors.DuplicateCode);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        var after = ToDto(plan, 0);
        await _audit.RecordAsync(new AuditEntry("subscription.plan_updated", "SubscriptionPlan", planId.ToString("D"), adminId, AuditActorType.Admin, before, after), cancellationToken);
        return Result.Success(after);
    }

    public async Task<Result<PlanDto>> SetStatusAsync(Guid planId, PlanStatus status, Guid adminId, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PlanDto>(PlanErrors.NotFound);
        }

        var before = plan.Status;
        var changed = plan.SetStatus(status, _clock.UtcNow);
        if (changed.IsFailure)
        {
            return Result.Failure<PlanDto>(changed.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(new AuditEntry("subscription.plan_status_changed", "SubscriptionPlan", planId.ToString("D"), adminId, AuditActorType.Admin,
            new { Status = before.ToString() }, new { Status = status.ToString() }), cancellationToken);
        return Result.Success(ToDto(plan, 0));
    }

    /// <summary>Makes this active plan the one owners without a subscription get (only one default at a time).</summary>
    public async Task<Result<PlanDto>> MakeDefaultAsync(Guid planId, Guid adminId, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PlanDto>(PlanErrors.NotFound);
        }

        if (plan.Status != PlanStatus.Active)
        {
            return Result.Failure<PlanDto>(PlanErrors.DefaultMustBeActive);
        }

        var now = _clock.UtcNow;
        foreach (var other in await _dbContext.Plans.Where(p => p.IsDefault && p.Id != planId).ToListAsync(cancellationToken))
        {
            other.SetDefault(false, now);
        }

        plan.SetDefault(true, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(new AuditEntry("subscription.plan_made_default", "SubscriptionPlan", planId.ToString("D"), adminId, AuditActorType.Admin), cancellationToken);
        return Result.Success(ToDto(plan, 0));
    }

    public static PlanDto ToDto(SubscriptionPlan p, int subscribers) => new(
        p.Id, p.Code, p.Name, p.Description, p.MonthlyPrice, p.AnnualPrice, p.Currency, p.MaxVenues, p.MaxPitches, p.MaxStaffPerVenue,
        p.Features.ToList(), p.TrialDays, p.GraceDays, p.IsPublic, p.IsDefault, p.SortOrder, p.Status.ToString(), subscribers, p.UpdatedAtUtc);
}
