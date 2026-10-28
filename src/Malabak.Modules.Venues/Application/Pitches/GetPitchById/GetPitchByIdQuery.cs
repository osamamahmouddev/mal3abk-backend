using Malabak.Modules.Venues.Application.Pitches;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.Pitches.GetPitchById;

public sealed record GetPitchByIdQuery(Guid PitchId, Guid UserId) : IQuery<Result<PitchDto>>;
