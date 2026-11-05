using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Matches.GetMatchById;

public sealed class GetMatchByIdHandler : IQueryHandler<GetMatchByIdQuery, Result<MatchDto>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;

    public GetMatchByIdHandler(IMatchesDbContext dbContext, ITeamCaptaincyChecker captaincyChecker)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
    }

    public async Task<Result<MatchDto>> HandleAsync(GetMatchByIdQuery query, CancellationToken cancellationToken = default)
    {
        var match = await _dbContext.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == query.MatchId, cancellationToken);

        if (match is null)
        {
            return Result.Failure<MatchDto>(MatchErrors.NotFound);
        }

        var homeTeamName = await _captaincyChecker.GetTeamNameAsync(match.HomeTeamId, cancellationToken) ?? "Home Team";
        var awayTeamName = await _captaincyChecker.GetTeamNameAsync(match.AwayTeamId, cancellationToken) ?? "Away Team";

        return Result.Success(MatchMapper.ToDto(match, homeTeamName, awayTeamName));
    }
}
