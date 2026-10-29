using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Venues;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;

namespace Malabak.Modules.Venues.Application.Venues.GetVenueById;

public sealed class GetVenueByIdHandler : IQueryHandler<GetVenueByIdQuery, Result<VenueDto>>
{
    private readonly IVenueRepository _venueRepository;
    private readonly IVenueAccessService _accessService;

    public GetVenueByIdHandler(IVenueRepository venueRepository, IVenueAccessService accessService)
    {
        _venueRepository = venueRepository;
        _accessService = accessService;
    }

    public async Task<Result<VenueDto>> HandleAsync(
        GetVenueByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(query.UserId, query.VenueId, cancellationToken);
        if (!access.Can(Permissions.VenueView))
        {
            return Result.Failure<VenueDto>(VenueAccessErrors.VenueNotFound);
        }

        var venue = await _venueRepository.GetByIdWithPitchesAsync(query.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<VenueDto>(VenueAccessErrors.VenueNotFound);
        }

        return Result.Success(VenueMapper.ToDto(venue));
    }
}
