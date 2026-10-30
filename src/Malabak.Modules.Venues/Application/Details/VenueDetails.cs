using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Reference;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Details;

public sealed record OpeningWindow(TimeOnly Open, TimeOnly Close);

/// <summary>Opening hours for one weekday (Cairo local time); a window closing before it opens runs past midnight.</summary>
public sealed record OpeningHoursDay(DayOfWeek Day, bool Closed, IReadOnlyList<OpeningWindow> Windows);

/// <summary>What a player needs to know about the deposit and cancellation before booking.</summary>
public sealed record PublicDepositPolicy(
    string DepositType,
    decimal DepositValue,
    int ProofSubmissionWindowMinutes,
    int CancellationDeadlineHours,
    string RefundEligibility,
    decimal RefundPercentage,
    string NoShowRule,
    int LateArrivalGraceMinutes,
    bool AllowReschedule,
    int RescheduleDeadlineHours,
    string? Terms);

public sealed record VenueDetailsDto(
    Guid VenueId,
    string Name,
    string Slug,
    string? Description,
    string Address,
    string City,
    string Area,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<string> Photos,
    IReadOnlyList<LocalizedItem> Facilities,
    IReadOnlyList<OpeningHoursDay> OpeningHours,
    decimal? FromHourlyPrice,
    string Currency,
    PublicDepositPolicy? DepositPolicy,
    int PitchCount,
    IReadOnlyList<string> PitchTypes,
    int FavoritesCount);

public sealed record PitchDetailsDto(
    Guid PitchId,
    string Name,
    string PitchType,
    int PlayersPerTeam,
    LocalizedItem Surface,
    decimal? LengthMeters,
    decimal? WidthMeters,
    IReadOnlyList<string> Photos,
    decimal? BaseHourlyPrice,
    string Currency,
    IReadOnlyList<OpeningHoursDay>? CustomHours);

public static class VenueDetailsErrors
{
    public static readonly Error NotFound = new("Venue.NotFound", "Venue not found.");
}

/// <summary>
/// Public venue profile and pitch showcase (TASK-023). Only approved and published venues are visible; unknown, draft,
/// rejected and unpublished venues all answer "not found" so their existence is not revealed. The owner's phone number
/// and internal fields are never part of the public profile.
/// </summary>
public sealed class VenueDetailsQuery
{
    private readonly VenuesDbContext _dbContext;
    private readonly IBookingPolicyProvider _policies;

    public VenueDetailsQuery(VenuesDbContext dbContext, IBookingPolicyProvider policies)
    {
        _dbContext = dbContext;
        _policies = policies;
    }

    public async Task<Result<VenueDetailsDto>> GetAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        var venue = await FindSearchableAsync(venueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<VenueDetailsDto>(VenueDetailsErrors.NotFound);
        }

        var pitches = await ActivePitchesAsync(venueId, cancellationToken);
        var rates = await BaseRatesAsync(venueId, cancellationToken);
        var schedules = await _dbContext.OperatingSchedules.AsNoTracking()
            .Where(s => s.VenueId == venueId && s.IsActive && !s.IsDeleted)
            .Select(s => new { s.DayOfWeek, s.OpenTime, s.CloseTime })
            .ToListAsync(cancellationToken);
        var favorites = await _dbContext.FavoriteVenues.AsNoTracking().CountAsync(f => f.VenueId == venueId, cancellationToken);
        var policy = await _policies.GetCurrentSnapshotAsync(venueId, cancellationToken);

        var prices = pitches.Select(p => BaseRate(rates, p.Id)).Where(r => r is not null).ToList();
        return Result.Success(new VenueDetailsDto(
            venue.Id, venue.Name, venue.Slug, venue.Description, venue.Address, venue.City, venue.Area, venue.Latitude, venue.Longitude,
            venue.Photos.ToList(),
            venue.Facilities.Select(LocalizeFacility).ToList(),
            Week(schedules.Select(s => (s.DayOfWeek, new OpeningWindow(s.OpenTime, s.CloseTime)))),
            prices.Min(), rates.FirstOrDefault()?.Currency ?? Money.DefaultCurrency,
            policy is null ? null : new PublicDepositPolicy(
                policy.DepositType, policy.DepositValue, policy.ProofSubmissionWindowMinutes, policy.CancellationDeadlineHours,
                policy.RefundEligibility, policy.RefundPercentage, policy.NoShowRule, policy.LateArrivalGraceMinutes,
                policy.AllowReschedule, policy.RescheduleDeadlineHours, policy.Terms),
            pitches.Count,
            pitches.Select(p => PitchType(p.Capacity)).Distinct().Order().ToList(),
            favorites));
    }

    public async Task<Result<IReadOnlyList<PitchDetailsDto>>> GetPitchesAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        if (await FindSearchableAsync(venueId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<PitchDetailsDto>>(VenueDetailsErrors.NotFound);
        }

        var pitches = await ActivePitchesAsync(venueId, cancellationToken);
        var rates = await BaseRatesAsync(venueId, cancellationToken);
        var pitchIds = pitches.Select(p => p.Id).ToList();
        var customHours = await _dbContext.PitchWeeklyHours.AsNoTracking()
            .Where(h => pitchIds.Contains(h.PitchId) && !h.IsDeleted)
            .ToListAsync(cancellationToken);
        var currency = rates.FirstOrDefault()?.Currency ?? Money.DefaultCurrency;

        IReadOnlyList<PitchDetailsDto> result = pitches
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p =>
            {
                var hours = customHours.Where(h => h.PitchId == p.Id).ToList();
                return new PitchDetailsDto(
                    p.Id, p.Name, PitchType(p.Capacity), p.Capacity, LocalizeSurface(p.TurfType), p.LengthMeters, p.WidthMeters,
                    p.Photos.ToList(), BaseRate(rates, p.Id), currency,
                    hours.Count == 0 ? null : Week(hours.Where(h => !h.IsClosed).Select(h => (h.DayOfWeek, new OpeningWindow(h.OpenTime, h.CloseTime)))));
            })
            .ToList();
        return Result.Success(result);
    }

    private Task<Venue?> FindSearchableAsync(Guid venueId, CancellationToken cancellationToken) =>
        _dbContext.Venues.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == venueId && v.ApprovalStatus == VenueApprovalStatus.Approved && v.IsPublished && !v.IsDeleted, cancellationToken);

    private Task<List<Pitch>> ActivePitchesAsync(Guid venueId, CancellationToken cancellationToken) =>
        _dbContext.Pitches.AsNoTracking().Where(p => p.VenueId == venueId && p.IsActive && !p.IsDeleted).ToListAsync(cancellationToken);

    private Task<List<BaseRateRow>> BaseRatesAsync(Guid venueId, CancellationToken cancellationToken) =>
        _dbContext.PriceRules.AsNoTracking()
            .Where(r => r.VenueId == venueId && r.Kind == PriceRuleKind.Base && r.IsActive && !r.IsDeleted && r.Amount != null)
            .Select(r => new BaseRateRow(r.PitchId, r.Amount!.Value, r.Currency))
            .ToListAsync(cancellationToken);

    /// <summary>Pitch override when present, else the venue-wide base rate.</summary>
    private static decimal? BaseRate(List<BaseRateRow> rates, Guid pitchId) =>
        rates.Where(r => r.PitchId == pitchId).Select(r => (decimal?)r.Amount).Min()
        ?? rates.Where(r => r.PitchId is null).Select(r => (decimal?)r.Amount).Min();

    private static List<OpeningHoursDay> Week(IEnumerable<(DayOfWeek Day, OpeningWindow Window)> windows)
    {
        var byDay = windows.ToLookup(w => w.Day, w => w.Window);
        return [.. Enum.GetValues<DayOfWeek>()
            .OrderBy(d => ((int)d + 1) % 7) // Saturday first, as the Egyptian week is shown
            .Select(d => new OpeningHoursDay(d, !byDay[d].Any(), byDay[d].OrderBy(w => w.Open).ToList()))];
    }

    private static string PitchType(int playersPerTeam) => $"{playersPerTeam}v{playersPerTeam}";

    private static LocalizedItem LocalizeFacility(string code) =>
        EgyptReferenceData.Facilities.FirstOrDefault(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? new LocalizedItem(code, code, code);

    private static LocalizedItem LocalizeSurface(string code) =>
        EgyptReferenceData.Surfaces.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? new LocalizedItem(code, code, code);

    private sealed record BaseRateRow(Guid? PitchId, decimal Amount, string Currency);
}
