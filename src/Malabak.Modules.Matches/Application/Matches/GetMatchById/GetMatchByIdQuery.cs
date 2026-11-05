using Malabak.Modules.Matches.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Application.Matches.GetMatchById;

public sealed record GetMatchByIdQuery(Guid MatchId) : IQuery<Result<MatchDto>>;
