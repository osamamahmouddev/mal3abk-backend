using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;

namespace Malabak.Modules.Venues.Application.Pitches.GetPitchById;

public sealed class GetPitchByIdHandler : IQueryHandler<GetPitchByIdQuery, Result<PitchDto>>
{
    private readonly IPitchRepository _pitchRepository;
    private readonly IVenueAccessService _accessService;

    public GetPitchByIdHandler(IPitchRepository pitchRepository, IVenueAccessService accessService)
    {
        _pitchRepository = pitchRepository;
        _accessService = accessService;
    }

    public async Task<Result<PitchDto>> HandleAsync(
        GetPitchByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var pitch = await _pitchRepository.GetByIdAsync(query.PitchId, cancellationToken);
        if (pitch is null)
        {
            return Result.Failure<PitchDto>(VenueAccessErrors.PitchNotFound);
        }

        var access = await _accessService.GetAccessAsync(query.UserId, pitch.VenueId, cancellationToken);
        if (!access.Can(Permissions.VenueView))
        {
            return Result.Failure<PitchDto>(VenueAccessErrors.PitchNotFound);
        }

        return Result.Success(PitchMapper.ToDto(pitch));
    }
}
