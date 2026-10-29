using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Schedules;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;

namespace Malabak.Modules.Venues.Application.Schedules.GetVenueSchedules;

public sealed class GetVenueSchedulesHandler : IQueryHandler<GetVenueSchedulesQuery, Result<IReadOnlyList<OperatingScheduleDto>>>
{
    private readonly IVenueRepository _venueRepository;
    private readonly IVenueAccessService _accessService;

    public GetVenueSchedulesHandler(IVenueRepository venueRepository, IVenueAccessService accessService)
    {
        _venueRepository = venueRepository;
        _accessService = accessService;
    }

    public async Task<Result<IReadOnlyList<OperatingScheduleDto>>> HandleAsync(
        GetVenueSchedulesQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(query.UserId, query.VenueId, cancellationToken);
        if (!access.Can(Permissions.VenueView))
        {
            return Result.Failure<IReadOnlyList<OperatingScheduleDto>>(VenueAccessErrors.VenueNotFound);
        }

        var venue = await _venueRepository.GetByIdWithSchedulesAsync(query.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<IReadOnlyList<OperatingScheduleDto>>(VenueAccessErrors.VenueNotFound);
        }

        var dtos = venue.OperatingSchedules
            .OrderBy(s => (int)s.DayOfWeek)
            .Select(ScheduleMapper.ToDto)
            .ToList();

        return Result.Success<IReadOnlyList<OperatingScheduleDto>>(dtos);
    }
}
