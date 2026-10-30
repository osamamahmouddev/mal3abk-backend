using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.SpecialSchedules;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Availability;

public enum SlotState
{
    Available = 0,

    /// <summary>A booking is in progress (awaiting deposit, proof submitted or under review) and has not lapsed.</summary>
    Held = 1,

    Booked = 2,

    /// <summary>Blocked by the venue (maintenance, private event).</summary>
    Blocked = 3,

    /// <summary>Too soon (minimum notice) or beyond the advance booking window.</summary>
    Unavailable = 4
}

public sealed record SlotDto(DateTime StartUtc, DateTime EndUtc, DateTime StartLocal, DateTime EndLocal, SlotState State, decimal? Price);

public sealed record PitchSlotsDto(Guid PitchId, string Name, int SlotMinutes, int IncrementMinutes, IReadOnlyList<SlotDto> Slots);

/// <param name="NextChangeUtc">Earliest moment the result changes by itself (a hold lapses or a slot passes the minimum notice).</param>
public sealed record VenueAvailabilityDto(
    Guid VenueId, DateOnly BusinessDate, string Currency, IReadOnlyList<PitchSlotsDto> Pitches, DateTime GeneratedAtUtc, DateTime? NextChangeUtc = null);

public static class AvailabilityErrors
{
    public static readonly Error VenueNotFound = new("Venue.NotFound", "Venue not found.");
    public static readonly Error DateOutOfRange = new("Availability.DateOutOfRange", "Choose a date from today up to the venue's advance booking window.");
}

/// <summary>
/// Public availability (TASK-021): for a business date, every pitch's open windows (weekly hours, pitch hours,
/// closures, special hours, breaks - overnight and DST aware) minus blocked slots, bookings and live holds, with a
/// server-calculated price per slot. Slots start on the pitch's increment grid and last its minimum duration.
/// Holds whose deposit window lapsed no longer block the slot.
/// </summary>
public sealed class VenueAvailabilityQuery
{
    private static readonly HashSet<string> _confirmedStatuses = new(StringComparer.Ordinal) { "Confirmed" };

    private readonly VenuesDbContext _dbContext;
    private readonly IUpcomingBookingsReader _bookings;
    private readonly IClock _clock;

    public VenueAvailabilityQuery(VenuesDbContext dbContext, IUpcomingBookingsReader bookings, IClock clock)
    {
        _dbContext = dbContext;
        _bookings = bookings;
        _clock = clock;
    }

    public async Task<Result<VenueAvailabilityDto>> GetAsync(Guid venueId, DateOnly businessDate, Guid? pitchId, CancellationToken cancellationToken = default)
    {
        var searchable = await _dbContext.Venues.AsNoTracking()
            .AnyAsync(v => v.Id == venueId && v.ApprovalStatus == VenueApprovalStatus.Approved && v.IsPublished && !v.IsDeleted, cancellationToken);
        if (!searchable)
        {
            return Result.Failure<VenueAvailabilityDto>(AvailabilityErrors.VenueNotFound);
        }

        var pitches = await _dbContext.Pitches.AsNoTracking()
            .Where(p => p.VenueId == venueId && p.IsActive && !p.IsDeleted && (pitchId == null || p.Id == pitchId))
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);
        if (pitchId is not null && pitches.Count == 0)
        {
            return Result.Failure<VenueAvailabilityDto>(VenueAccessErrors.PitchNotFound);
        }

        var states = await ScheduleStateLoader.LoadAsync(_dbContext, venueId, pitchId, cancellationToken);
        var maxAdvance = states.Count == 0 ? BookingRules.DefaultMaxAdvanceDays : states.Values.Max(s => s.Rules.MaxAdvanceDays);
        var today = _clock.CairoToday;
        if (businessDate < today.AddDays(-1) || businessDate > today.AddDays(maxAdvance))
        {
            return Result.Failure<VenueAvailabilityDto>(AvailabilityErrors.DateOutOfRange);
        }

        var tz = _clock.CairoTimeZone;
        var priceRules = await _dbContext.PriceRules.AsNoTracking()
            .Where(r => r.VenueId == venueId && r.IsActive && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        // Bookings/blocks overlapping this business date's open hours (which may run past midnight).
        var rangeStart = OperatingSchedule.ConvertToUtc(businessDate.ToDateTime(TimeOnly.MinValue), tz);
        var rangeEnd = OperatingSchedule.ConvertToUtc(businessDate.AddDays(2).ToDateTime(TimeOnly.MinValue), tz);
        var pitchIds = pitches.Select(p => p.Id).ToList();
        var bookings = pitchIds.Count == 0 ? [] : await _bookings.GetActiveAsync(pitchIds, rangeStart, rangeEnd, cancellationToken);
        var blocks = await _dbContext.BlockedSlots.AsNoTracking()
            .Where(b => b.VenueId == venueId && !b.IsDeleted && b.EndUtc > rangeStart && b.StartUtc < rangeEnd)
            .Select(b => new { b.PitchId, b.StartUtc, b.EndUtc })
            .ToListAsync(cancellationToken);

        var nowUtc = _clock.UtcNow;
        DateTime? nextChange = bookings.Where(b => b.HoldExpiresAtUtc > nowUtc).Select(b => b.HoldExpiresAtUtc).Min();
        var result = new List<PitchSlotsDto>();
        foreach (var pitch in pitches)
        {
            if (!states.TryGetValue(pitch.Id, out var state))
            {
                continue;
            }

            var rules = state.Rules;
            var slots = new List<SlotDto>();
            foreach (var window in AvailabilityCalculator.OpenWindows(state, businessDate, tz))
            {
                for (var start = window.StartUtc; start.AddMinutes(rules.MinDurationMinutes) <= window.EndUtc; start = start.AddMinutes(rules.IncrementMinutes))
                {
                    var end = start.AddMinutes(rules.MinDurationMinutes);
                    var slotState = SlotStateFor(start, nowUtc, rules,
                        bookings.Where(b => b.PitchId == pitch.Id && b.StartUtc < end && b.EndUtc > start).ToList(),
                        blocks.Exists(b => b.PitchId == pitch.Id && b.StartUtc < end && b.EndUtc > start));
                    if (slotState == SlotState.Available)
                    {
                        var noticeCrossing = start.AddMinutes(-rules.MinNoticeMinutes);
                        nextChange = nextChange is null || noticeCrossing < nextChange ? noticeCrossing : nextChange;
                    }

                    var price = PricingCalculator.Quote(pitch.Id, priceRules, start, end, null, tz);
                    slots.Add(new SlotDto(start, end, TimeZoneInfo.ConvertTimeFromUtc(start, tz), TimeZoneInfo.ConvertTimeFromUtc(end, tz),
                        slotState, price.IsSuccess ? price.Value.Total : null));
                }
            }

            result.Add(new PitchSlotsDto(pitch.Id, pitch.Name, rules.MinDurationMinutes, rules.IncrementMinutes, slots));
        }

        var currency = priceRules.Select(r => r.Currency).FirstOrDefault() ?? Money.DefaultCurrency;
        return Result.Success(new VenueAvailabilityDto(venueId, businessDate, currency, result, nowUtc, nextChange));
    }

    private static SlotState SlotStateFor(DateTime start, DateTime nowUtc, BookingRules rules, List<UpcomingBookingRef> overlapping, bool blocked)
    {
        if (overlapping.Exists(b => _confirmedStatuses.Contains(b.Status)))
        {
            return SlotState.Booked;
        }

        if (overlapping.Exists(b => b.HoldExpiresAtUtc is null || b.HoldExpiresAtUtc > nowUtc))
        {
            return SlotState.Held;
        }

        if (blocked)
        {
            return SlotState.Blocked;
        }

        return start < nowUtc.AddMinutes(rules.MinNoticeMinutes) || start > nowUtc.AddDays(rules.MaxAdvanceDays)
            ? SlotState.Unavailable
            : SlotState.Available;
    }
}
