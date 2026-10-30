using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Pricing;

public sealed record PriceRuleDto(
    Guid Id,
    Guid? PitchId,
    string Kind,
    string Adjustment,
    string Name,
    decimal? Amount,
    string Currency,
    decimal? Percent,
    IReadOnlyList<string>? Days,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? MinDurationMinutes,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record PricePreviewDto(PriceQuoteSnapshot Current, PriceQuoteSnapshot? WithDraft, decimal? Difference);

/// <summary>
/// Price rules management for venue managers (TASK-219): list, create, update, delete with validation and overlap checks,
/// audit logging, and a preview that prices a slot with the current rules and with a draft rule. Rule changes only affect
/// new quotes; bookings keep the price agreed at booking time.
/// </summary>
public sealed class PriceRuleManagement
{
    private readonly VenuesDbContext _dbContext;
    private readonly IVenueAccessService _access;
    private readonly IVenueAuditLogger _audit;
    private readonly PricingService _pricing;
    private readonly IClock _clock;

    public PriceRuleManagement(VenuesDbContext dbContext, IVenueAccessService access, IVenueAuditLogger audit, PricingService pricing, IClock clock)
    {
        _dbContext = dbContext;
        _access = access;
        _audit = audit;
        _pricing = pricing;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<PriceRuleDto>>> ListAsync(Guid venueId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!(await _access.GetAccessAsync(userId, venueId, cancellationToken)).Can(Permissions.VenueView))
        {
            return Result.Failure<IReadOnlyList<PriceRuleDto>>(VenueAccessErrors.VenueNotFound);
        }

        var rules = await _dbContext.PriceRules.AsNoTracking()
            .Where(r => r.VenueId == venueId && !r.IsDeleted)
            .OrderBy(r => r.Kind).ThenBy(r => r.PitchId).ThenBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<PriceRuleDto>>(rules.Select(ToDto).ToList());
    }

    public async Task<Result<PriceRuleDto>> CreateAsync(Guid venueId, Guid userId, PriceRuleTerms terms, CancellationToken cancellationToken = default)
    {
        var denied = await AuthorizeAsync(venueId, userId, terms.PitchId, cancellationToken);
        if (denied is not null)
        {
            return Result.Failure<PriceRuleDto>(denied);
        }

        var created = PriceRule.Create(venueId, terms, _clock.UtcNow);
        if (created.IsFailure)
        {
            return Result.Failure<PriceRuleDto>(created.Error);
        }

        var conflict = await CheckConsistencyAsync(venueId, created.Value, cancellationToken);
        if (conflict is not null)
        {
            return Result.Failure<PriceRuleDto>(conflict);
        }

        _dbContext.PriceRules.Add(created.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("PriceRuleCreated", nameof(PriceRule), created.Value.Id, userId, ToDto(created.Value), cancellationToken);
        return Result.Success(ToDto(created.Value));
    }

    public async Task<Result<PriceRuleDto>> UpdateAsync(
        Guid venueId, Guid userId, Guid ruleId, PriceRuleTerms terms, bool isActive, CancellationToken cancellationToken = default)
    {
        var denied = await AuthorizeAsync(venueId, userId, terms.PitchId, cancellationToken);
        if (denied is not null)
        {
            return Result.Failure<PriceRuleDto>(denied);
        }

        var rule = await _dbContext.PriceRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.VenueId == venueId && !r.IsDeleted, cancellationToken);
        if (rule is null)
        {
            return Result.Failure<PriceRuleDto>(PriceRuleErrors.NotFound);
        }

        var before = ToDto(rule);
        var updated = rule.Update(terms, isActive, _clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result.Failure<PriceRuleDto>(updated.Error);
        }

        var conflict = await CheckConsistencyAsync(venueId, rule, cancellationToken);
        if (conflict is not null)
        {
            return Result.Failure<PriceRuleDto>(conflict);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("PriceRuleUpdated", nameof(PriceRule), rule.Id, userId, new { before, after = ToDto(rule) }, cancellationToken);
        return Result.Success(ToDto(rule));
    }

    public async Task<Result> DeleteAsync(Guid venueId, Guid userId, Guid ruleId, CancellationToken cancellationToken = default)
    {
        var denied = await AuthorizeAsync(venueId, userId, null, cancellationToken);
        if (denied is not null)
        {
            return Result.Failure(denied);
        }

        var rule = await _dbContext.PriceRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.VenueId == venueId && !r.IsDeleted, cancellationToken);
        if (rule is null)
        {
            return Result.Failure(PriceRuleErrors.NotFound);
        }

        rule.MarkDeleted(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("PriceRuleDeleted", nameof(PriceRule), rule.Id, userId, ToDto(rule), cancellationToken);
        return Result.Success();
    }

    /// <summary>Prices a slot with the saved rules and, when <paramref name="draft"/> is given, as if the draft rule were saved too.</summary>
    public async Task<Result<PricePreviewDto>> PreviewAsync(
        Guid venueId, Guid userId, Guid pitchId, DateTime startUtc, DateTime endUtc, PriceRuleTerms? draft, CancellationToken cancellationToken = default)
    {
        if (!(await _access.GetAccessAsync(userId, venueId, cancellationToken)).Can(Permissions.VenueView))
        {
            return Result.Failure<PricePreviewDto>(VenueAccessErrors.VenueNotFound);
        }

        if (!await _dbContext.Pitches.AnyAsync(p => p.Id == pitchId && p.VenueId == venueId && !p.IsDeleted, cancellationToken))
        {
            return Result.Failure<PricePreviewDto>(VenueAccessErrors.PitchNotFound);
        }

        var rules = await _pricing.LoadRulesAsync(venueId, cancellationToken);
        var deposit = await _pricing.LoadDepositAsync(venueId, cancellationToken);
        var current = PricingCalculator.Quote(pitchId, rules, startUtc, endUtc, deposit, _clock.CairoTimeZone);
        if (current.IsFailure)
        {
            return Result.Failure<PricePreviewDto>(current.Error);
        }

        if (draft is null)
        {
            return Result.Success(new PricePreviewDto(_pricing.ToSnapshot(venueId, current.Value), null, null));
        }

        var draftRule = PriceRule.Create(venueId, draft, _clock.UtcNow);
        if (draftRule.IsFailure)
        {
            return Result.Failure<PricePreviewDto>(draftRule.Error);
        }

        var withDraft = PricingCalculator.Quote(pitchId, [.. rules, draftRule.Value], startUtc, endUtc, deposit, _clock.CairoTimeZone);
        if (withDraft.IsFailure)
        {
            return Result.Failure<PricePreviewDto>(withDraft.Error);
        }

        return Result.Success(new PricePreviewDto(
            _pricing.ToSnapshot(venueId, current.Value), _pricing.ToSnapshot(venueId, withDraft.Value), withDraft.Value.Total - current.Value.Total));
    }

    private async Task<Error?> AuthorizeAsync(Guid venueId, Guid userId, Guid? pitchId, CancellationToken cancellationToken)
    {
        if (!(await _access.GetAccessAsync(userId, venueId, cancellationToken)).Can(Permissions.VenueEdit))
        {
            return VenueAccessErrors.VenueNotFound;
        }

        return pitchId is null || await _dbContext.Pitches.AnyAsync(p => p.Id == pitchId && p.VenueId == venueId && !p.IsDeleted, cancellationToken)
            ? null
            : VenueAccessErrors.PitchNotFound;
    }

    /// <summary>Same-kind, same-scope rules must not overlap, and all amounts of a venue share one currency.</summary>
    private async Task<Error?> CheckConsistencyAsync(Guid venueId, PriceRule candidate, CancellationToken cancellationToken)
    {
        if (!candidate.IsActive)
        {
            return null;
        }

        var others = await _dbContext.PriceRules.AsNoTracking()
            .Where(r => r.VenueId == venueId && r.Id != candidate.Id && r.IsActive && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        if (others.Exists(o => o.Price is not null && candidate.Price is not null && o.Currency != candidate.Currency))
        {
            return PriceRuleErrors.CurrencyMismatch;
        }

        // Discounts may overlap (the best one applies); every other kind must be unambiguous.
        return candidate.Kind != PriceRuleKind.Discount && others.Exists(candidate.Overlaps) ? PriceRuleErrors.Overlap : null;
    }

    public static PriceRuleDto ToDto(PriceRule r) => new(
        r.Id, r.PitchId, r.Kind.ToString(), r.Adjustment.ToString(), r.Name, r.Amount, r.Currency, r.Percent,
        r.Days is { } days ? Enum.GetValues<DayOfWeek>().Where(d => days.Includes(d)).Select(d => d.ToString()).ToList() : null,
        r.StartTime, r.EndTime, r.StartDate, r.EndDate, r.MinDurationMinutes, r.IsActive, r.CreatedAtUtc, r.UpdatedAtUtc);
}
