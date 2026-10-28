using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Pitches.GetPitchById;

public sealed class GetPitchByIdHandler : IQueryHandler<GetPitchByIdQuery, Result<PitchDto>>
{
    private readonly IPitchRepository _pitchRepository;

    public GetPitchByIdHandler(IPitchRepository pitchRepository)
    {
        _pitchRepository = pitchRepository;
    }

    public async Task<Result<PitchDto>> HandleAsync(
        GetPitchByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var pitch = await _pitchRepository.GetByIdAsync(query.PitchId, cancellationToken);
        if (pitch is null)
        {
            return Result.Failure<PitchDto>(new Error("Pitch.NotFound", "Pitch not found."));
        }

        return Result.Success(PitchMapper.ToDto(pitch));
    }
}
