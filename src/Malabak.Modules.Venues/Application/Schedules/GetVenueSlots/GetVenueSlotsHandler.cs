using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Schedules;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Schedules.GetVenueSlots;

public sealed class GetVenueSlotsHandler : IQueryHandler<GetVenueSlotsQuery, Result<IReadOnlyList<ScheduleSlotDto>>>
{
    private readonly IVenueRepository _venueRepository;
    private readonly IClock _clock;

    public GetVenueSlotsHandler(IVenueRepository venueRepository, IClock clock)
    {
        _venueRepository = venueRepository;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ScheduleSlotDto>>> HandleAsync(
        GetVenueSlotsQuery query,
        CancellationToken cancellationToken = default)
    {
        var venue = await _venueRepository.GetByIdWithSchedulesAsync(query.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<IReadOnlyList<ScheduleSlotDto>>(new Error("Venue.NotFound", "Venue not found."));
        }

        var schedule = venue.OperatingSchedules
            .FirstOrDefault(s => s.DayOfWeek == query.Date.DayOfWeek && s.IsActive && !s.IsDeleted);

        if (schedule is null)
        {
            return Result.Success<IReadOnlyList<ScheduleSlotDto>>(Array.Empty<ScheduleSlotDto>());
        }

        var slots = schedule.GenerateSlots(query.Date, _clock.CairoTimeZone);
        var dtos = slots.Select(ScheduleMapper.ToDto).ToList();

        return Result.Success<IReadOnlyList<ScheduleSlotDto>>(dtos);
    }
}
