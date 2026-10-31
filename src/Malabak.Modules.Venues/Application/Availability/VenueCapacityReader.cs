using Malabak.Modules.Venues.Application.SpecialSchedules;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Availability;

/// <summary>Open pitch-minutes per business date (occupancy denominator for the venue dashboard and reports).</summary>
public sealed class VenueCapacityReader : IVenueCapacityReader
{
    /// <summary>Longest range one call may cover; reports are limited to a year.</summary>
    public const int MaxDays = 366;

    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;

    public VenueCapacityReader(VenuesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<IReadOnlyDictionary<DateOnly, int>> GetOpenMinutesAsync(
        Guid venueId, DateOnly from, DateOnly to, Guid? pitchId = null, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<DateOnly, int>();
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
        {
            return result;
        }

        var activePitches = await _dbContext.Pitches.AsNoTracking()
            .Where(p => p.VenueId == venueId && p.IsActive && !p.IsDeleted && (pitchId == null || p.Id == pitchId))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        var states = await ScheduleStateLoader.LoadAsync(_dbContext, venueId, pitchId, cancellationToken);
        var tz = _clock.CairoTimeZone;

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var minutes = 0;
            foreach (var id in activePitches)
            {
                if (states.TryGetValue(id, out var state))
                {
                    minutes += AvailabilityCalculator.OpenWindows(state, day, tz).Sum(w => (int)(w.EndUtc - w.StartUtc).TotalMinutes);
                }
            }

            result[day] = minutes;
        }

        return result;
    }

    public async Task<IReadOnlyList<PitchOpenMinutes>> GetOpenMinutesByPitchAsync(
        Guid venueId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
        {
            return [];
        }

        var pitches = await _dbContext.Pitches.AsNoTracking()
            .Where(p => p.VenueId == venueId && p.IsActive && !p.IsDeleted)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);
        var states = await ScheduleStateLoader.LoadAsync(_dbContext, venueId, null, cancellationToken);
        var tz = _clock.CairoTimeZone;

        return pitches.Select(p =>
        {
            var minutes = 0;
            if (states.TryGetValue(p.Id, out var state))
            {
                for (var day = from; day <= to; day = day.AddDays(1))
                {
                    minutes += AvailabilityCalculator.OpenWindows(state, day, tz).Sum(w => (int)(w.EndUtc - w.StartUtc).TotalMinutes);
                }
            }

            return new PitchOpenMinutes(p.Id, p.Name, minutes);
        }).ToList();
    }
}
