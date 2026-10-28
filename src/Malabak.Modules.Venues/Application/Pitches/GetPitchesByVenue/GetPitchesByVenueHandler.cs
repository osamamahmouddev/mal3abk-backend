using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;

namespace Malabak.Modules.Venues.Application.Pitches.GetPitchesByVenue;

public sealed class GetPitchesByVenueHandler : IQueryHandler<GetPitchesByVenueQuery, Result<IReadOnlyList<PitchDto>>>
{
    private readonly IPitchRepository _pitchRepository;
    private readonly IVenueAccessService _accessService;

    public GetPitchesByVenueHandler(IPitchRepository pitchRepository, IVenueAccessService accessService)
    {
        _pitchRepository = pitchRepository;
        _accessService = accessService;
    }

    public async Task<Result<IReadOnlyList<PitchDto>>> HandleAsync(
        GetPitchesByVenueQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(query.UserId, query.VenueId, cancellationToken);
        if (!access.Can(Permissions.VenueView))
        {
            return Result.Failure<IReadOnlyList<PitchDto>>(VenueAccessErrors.VenueNotFound);
        }

        var pitches = await _pitchRepository.GetByVenueIdAsync(query.VenueId, cancellationToken);
        IReadOnlyList<PitchDto> dtos = pitches.Select(PitchMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}
