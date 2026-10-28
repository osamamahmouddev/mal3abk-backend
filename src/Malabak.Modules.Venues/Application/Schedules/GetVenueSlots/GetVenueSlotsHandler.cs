using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Schedules;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;

namespace Malabak.Modules.Venues.Application.Schedules.GetVenueSlots;

public sealed class GetVenueSlotsHandler : IQueryHandler<GetVenueSlotsQuery, Result<IReadOnlyList<ScheduleSlotDto>>>
{
    private readonly IVenueRepository _venueRepository;
    private readonly IVenueAccessService _accessService;
    private readonly IClock _clock;

    public GetVenueSlotsHandler(IVenueRepository venueRepository, IVenueAccessService accessService, IClock clock)
    {
        _venueRepository = venueRepository;
        _accessService = accessService;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ScheduleSlotDto>>> HandleAsync(
        GetVenueSlotsQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(query.UserId, query.VenueId, cancellationToken);
        if (!access.Can(Permissions.VenueView))
        {
            return Result.Failure<IReadOnlyList<ScheduleSlotDto>>(VenueAccessErrors.VenueNotFound);
        }

        var venue = await _venueRepository.GetByIdWithSchedulesAsync(query.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<IReadOnlyList<ScheduleSlotDto>>(VenueAccessErrors.VenueNotFound);
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
