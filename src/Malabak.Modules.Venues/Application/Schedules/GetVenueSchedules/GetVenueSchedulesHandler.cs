using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Schedules;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Schedules.GetVenueSchedules;

public sealed class GetVenueSchedulesHandler : IQueryHandler<GetVenueSchedulesQuery, Result<IReadOnlyList<OperatingScheduleDto>>>
{
    private readonly IVenueRepository _venueRepository;

    public GetVenueSchedulesHandler(IVenueRepository venueRepository)
    {
        _venueRepository = venueRepository;
    }

    public async Task<Result<IReadOnlyList<OperatingScheduleDto>>> HandleAsync(
        GetVenueSchedulesQuery query,
        CancellationToken cancellationToken = default)
    {
        var venue = await _venueRepository.GetByIdWithSchedulesAsync(query.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<IReadOnlyList<OperatingScheduleDto>>(new Error("Venue.NotFound", "Venue not found."));
        }

        var dtos = venue.OperatingSchedules
            .OrderBy(s => (int)s.DayOfWeek)
            .Select(ScheduleMapper.ToDto)
            .ToList();

        return Result.Success<IReadOnlyList<OperatingScheduleDto>>(dtos);
    }
}
