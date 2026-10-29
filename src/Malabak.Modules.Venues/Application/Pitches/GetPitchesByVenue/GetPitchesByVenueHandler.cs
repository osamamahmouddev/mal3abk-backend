using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Pitches.GetPitchesByVenue;

public sealed class GetPitchesByVenueHandler : IQueryHandler<GetPitchesByVenueQuery, Result<IReadOnlyList<PitchDto>>>
{
    private readonly IPitchRepository _pitchRepository;

    public GetPitchesByVenueHandler(IPitchRepository pitchRepository)
    {
        _pitchRepository = pitchRepository;
    }

    public async Task<Result<IReadOnlyList<PitchDto>>> HandleAsync(
        GetPitchesByVenueQuery query,
        CancellationToken cancellationToken = default)
    {
        var pitches = await _pitchRepository.GetByVenueIdAsync(query.VenueId, cancellationToken);
        var dtos = pitches.Select(PitchMapper.ToDto).ToList() as IReadOnlyList<PitchDto>;
        return Result.Success(dtos);
    }
}
