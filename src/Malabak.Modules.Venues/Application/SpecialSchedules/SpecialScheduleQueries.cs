using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.SpecialSchedules;

public sealed record VenueWeeklyHoursInput(DayOfWeek DayOfWeek, TimeOnly OpenTime, TimeOnly CloseTime, bool IsActive);

/// <summary>Read side of special schedules, plus the impact preview for a change to the venue's recurring weekly hours.</summary>
public sealed class SpecialScheduleQueries
{
    private readonly VenuesDbContext _dbContext;
    private readonly IVenueAccessService _access;
    private readonly IUpcomingBookingsReader _bookings;
    private readonly IClock _clock;

    public SpecialScheduleQueries(VenuesDbContext dbContext, IVenueAccessService access, IUpcomingBookingsReader bookings, IClock clock)
    {
        _dbContext = dbContext;
        _access = access;
        _bookings = bookings;
        _clock = clock;
    }

    public async Task<Result<SpecialSchedulesDto>> GetAsync(Guid venueId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!(await _access.GetAccessAsync(userId, venueId, cancellationToken)).Can(Permissions.VenueView))
        {
            return Result.Failure<SpecialSchedulesDto>(VenueAccessErrors.VenueNotFound);
        }

        var exceptions = await _dbContext.ScheduleOverrides.AsNoTracking()
            .Where(e => e.VenueId == venueId && !e.IsDeleted && e.EndDate >= _clock.CairoToday)
            .OrderBy(e => e.StartDate)
            .ToListAsync(cancellationToken);
        var hours = await _dbContext.PitchWeeklyHours.AsNoTracking().Where(h => h.VenueId == venueId && !h.IsDeleted).ToListAsync(cancellationToken);
        var rules = await _dbContext.BookingRules.AsNoTracking().Where(r => r.VenueId == venueId).ToListAsync(cancellationToken);
        if (!rules.Exists(r => r.PitchId == null))
        {
            rules.Insert(0, BookingRules.Defaults(venueId));
        }

        return Result.Success(new SpecialSchedulesDto(
            exceptions.Select(e => new ScheduleOverrideDto(e.Id, e.PitchId, e.StartDate, e.EndDate, e.Kind.ToString(), e.FromTime, e.ToTime, e.Reason)).ToList(),
            hours.OrderBy(h => h.PitchId).ThenBy(h => h.DayOfWeek)
                .Select(h => new PitchWeeklyHoursDto(h.PitchId, h.DayOfWeek, h.OpenTime, h.CloseTime, h.IsClosed)).ToList(),
            rules.Select(ToDto).ToList()));
    }

    public async Task<Result<PitchAvailabilityDto>> GetPitchAvailabilityAsync(
        Guid venueId, Guid pitchId, DateOnly businessDate, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!(await _access.GetAccessAsync(userId, venueId, cancellationToken)).Can(Permissions.VenueView))
        {
            return Result.Failure<PitchAvailabilityDto>(VenueAccessErrors.VenueNotFound);
        }

        var states = await ScheduleStateLoader.LoadAsync(_dbContext, venueId, pitchId, cancellationToken);
        if (!states.TryGetValue(pitchId, out var state))
        {
            return Result.Failure<PitchAvailabilityDto>(VenueAccessErrors.PitchNotFound);
        }

        var tz = _clock.CairoTimeZone;
        var windows = AvailabilityCalculator.OpenWindows(state, businessDate, tz);
        var starts = AvailabilityCalculator.StartTimes(windows, state.Rules, _clock.UtcNow)
            .Select(s => new AvailableStartDto(s, TimeZoneInfo.ConvertTimeFromUtc(s, tz)))
            .ToList();
        return Result.Success(new PitchAvailabilityDto(pitchId, businessDate, windows, starts, ToDto(state.Rules)));
    }

    /// <summary>Which future bookings would fall outside the proposed venue weekly hours (nothing is saved).</summary>
    public async Task<Result<IReadOnlyList<UpcomingBookingRef>>> PreviewVenueWeeklyChangeAsync(
        Guid venueId, Guid userId, IReadOnlyList<VenueWeeklyHoursInput> days, CancellationToken cancellationToken = default)
    {
        if (!(await _access.GetAccessAsync(userId, venueId, cancellationToken)).Can(Permissions.SchedulesManage))
        {
            return Result.Failure<IReadOnlyList<UpcomingBookingRef>>(VenueAccessErrors.VenueNotFound);
        }

        var states = await ScheduleStateLoader.LoadAsync(_dbContext, venueId, null, cancellationToken);
        if (states.Count == 0)
        {
            return Result.Success<IReadOnlyList<UpcomingBookingRef>>([]);
        }

        var nowUtc = _clock.UtcNow;
        var proposedWeek = states.Values.First().VenueWeek
            .Where(s => !days.Any(d => d.DayOfWeek == s.DayOfWeek))
            .Concat(days.Where(d => d.IsActive).Select(d => OperatingSchedule.Create(Guid.Empty, venueId, d.DayOfWeek, d.OpenTime, d.CloseTime, 60, nowUtc)))
            .ToList();

        var tz = _clock.CairoTimeZone;
        var affected = (await _bookings.GetActiveAsync(states.Keys, nowUtc, nowUtc.AddDays(SpecialScheduleHandlers.RecurringImpactDays), cancellationToken))
            .Where(b => states.TryGetValue(b.PitchId, out var state)
                && AvailabilityCalculator.Fits(state, b.StartUtc, b.EndUtc, tz)
                && !AvailabilityCalculator.Fits(state with { VenueWeek = proposedWeek }, b.StartUtc, b.EndUtc, tz))
            .ToList();
        return Result.Success<IReadOnlyList<UpcomingBookingRef>>(affected);
    }

    internal static BookingRulesDto ToDto(BookingRules rules) => new(
        rules.PitchId, rules.MinDurationMinutes, rules.MaxDurationMinutes, rules.IncrementMinutes, rules.MinNoticeMinutes, rules.MaxAdvanceDays);
}

/// <summary>Implements the Venues contract the Booking module uses to validate requested slots server-side.</summary>
public sealed class PitchAvailabilityChecker : IPitchAvailabilityChecker
{
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;

    public PitchAvailabilityChecker(VenuesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> CheckSlotAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var venueId = await _dbContext.Pitches.AsNoTracking()
            .Where(p => p.Id == pitchId && p.IsActive && !p.IsDeleted)
            .Select(p => (Guid?)p.VenueId)
            .FirstOrDefaultAsync(cancellationToken);
        if (venueId is null)
        {
            return Result.Failure(VenueAccessErrors.PitchNotFound);
        }

        var states = await ScheduleStateLoader.LoadAsync(_dbContext, venueId.Value, pitchId, cancellationToken);
        var error = AvailabilityCalculator.CheckSlot(states[pitchId], startUtc, endUtc, _clock.UtcNow, _clock.CairoTimeZone);
        return error is null ? Result.Success() : Result.Failure(error);
    }
}
