using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Venues;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Venues.GetVenueById;

public sealed class GetVenueByIdHandler : IQueryHandler<GetVenueByIdQuery, Result<VenueDto>>
{
    private readonly IVenueRepository _venueRepository;

    public GetVenueByIdHandler(IVenueRepository venueRepository)
    {
        _venueRepository = venueRepository;
    }

    public async Task<Result<VenueDto>> HandleAsync(
        GetVenueByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var venue = await _venueRepository.GetByIdWithPitchesAsync(query.VenueId, cancellationToken);
        if (venue is null)
        {
            return Result.Failure<VenueDto>(new Error(
                "Venue.NotFound",
                "Venue not found."));
        }

        return Result.Success(VenueMapper.ToDto(venue));
    }
}
