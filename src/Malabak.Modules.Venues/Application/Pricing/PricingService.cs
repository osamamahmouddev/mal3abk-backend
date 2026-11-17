using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Pricing;

/// <summary>
/// Loads a pitch's price rules and the venue's current booking policy (deposit) and asks <see cref="PricingCalculator"/>
/// for the price. This is the single pricing entry point for quotes, availability and booking creation.
/// </summary>
public sealed class PricingService : IPriceQuoteProvider
{
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IPlatformSettings? _platformSettings;
    private readonly ILogger<PricingService>? _logger;

    public PricingService(
        VenuesDbContext dbContext,
        IClock clock,
        IPlatformSettings? platformSettings = null,
        ILogger<PricingService>? logger = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _platformSettings = platformSettings;
        _logger = logger;
    }

    public async Task<Result<PriceQuoteSnapshot>> QuoteAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var venueId = await _dbContext.Pitches.AsNoTracking()
            .Where(p => p.Id == pitchId && p.IsActive && !p.IsDeleted)
            .Select(p => (Guid?)p.VenueId)
            .FirstOrDefaultAsync(cancellationToken);
        if (venueId is null)
        {
            return Result.Failure<PriceQuoteSnapshot>(VenueAccessErrors.PitchNotFound);
        }

        var venueCommission = await _dbContext.Venues.AsNoTracking()
            .Where(v => v.Id == venueId.Value)
            .Select(v => new { v.CustomCommissionEnabled, v.CommissionType, v.CommissionRate })
            .FirstOrDefaultAsync(cancellationToken);

        var defaultHourlyCommission = 0m;
        if (_platformSettings is not null)
        {
            try
            {
                defaultHourlyCommission = await _platformSettings.GetDecimalAsync(PlatformSettingKeys.DefaultHourlyCommission, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to retrieve default hourly commission from platform settings. Falling back to 0.");
                defaultHourlyCommission = 0m;
            }
        }

        var rules = await LoadRulesAsync(venueId.Value, cancellationToken);
        var deposit = await LoadDepositAsync(venueId.Value, cancellationToken);
        var commissionType = venueCommission?.CustomCommissionEnabled == true
            ? venueCommission.CommissionType
            : CommissionType.PlatformDefault;
        var commissionRate = venueCommission?.CustomCommissionEnabled == true
            ? venueCommission.CommissionRate
            : null;

        var quote = PricingCalculator.Quote(
            pitchId,
            rules,
            startUtc,
            endUtc,
            deposit,
            _clock.CairoTimeZone,
            commissionType,
            commissionRate,
            defaultHourlyCommission);

        return quote.IsFailure ? Result.Failure<PriceQuoteSnapshot>(quote.Error) : Result.Success(ToSnapshot(venueId.Value, quote.Value));
    }

    public Task<List<PriceRule>> LoadRulesAsync(Guid venueId, CancellationToken cancellationToken) =>
        _dbContext.PriceRules.AsNoTracking().Where(r => r.VenueId == venueId && r.IsActive && !r.IsDeleted).ToListAsync(cancellationToken);

    public async Task<DepositRule?> LoadDepositAsync(Guid venueId, CancellationToken cancellationToken)
    {
        var policy = await _dbContext.Policies.AsNoTracking()
            .Where(p => p.VenueId == venueId && p.IsCurrent)
            .Select(p => new { p.DepositType, p.DepositValue })
            .FirstOrDefaultAsync(cancellationToken);
        return policy is null ? null : DepositRule.Create(policy.DepositType, policy.DepositValue);
    }

    public PriceQuoteSnapshot ToSnapshot(Guid venueId, PriceQuote quote) => new(
        venueId, quote.PitchId, quote.StartUtc, quote.EndUtc, quote.DurationMinutes, quote.Currency, quote.Subtotal, quote.Discount,
        quote.DiscountRule, quote.Total, quote.OnlineDepositAmount, quote.DepositType.ToString(),
        quote.Lines.Select(l => new PriceLineSnapshot(l.StartUtc, l.EndUtc, l.HourlyRate, l.Amount, l.AppliedRules)).ToList(),
        _clock.UtcNow,
        quote.NetPitchPrice,
        quote.PlatformCommission,
        quote.OnlineDepositAmount,
        quote.VenuePayAmount);
}
