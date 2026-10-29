using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Venues;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Venues.GetVenuesByOwner;

public sealed class GetVenuesByOwnerHandler : IQueryHandler<GetVenuesByOwnerQuery, Result<IReadOnlyList<VenueDto>>>
{
    private readonly IVenueRepository _venueRepository;

    public GetVenuesByOwnerHandler(IVenueRepository venueRepository)
    {
        _venueRepository = venueRepository;
    }

    public async Task<Result<IReadOnlyList<VenueDto>>> HandleAsync(
        GetVenuesByOwnerQuery query,
        CancellationToken cancellationToken = default)
    {
        var venues = await _venueRepository.GetByOwnerIdAsync(query.OwnerId, cancellationToken);
        var dtos = venues.Select(VenueMapper.ToDto).ToList();

        return Result.Success<IReadOnlyList<VenueDto>>(dtos);
    }
}
