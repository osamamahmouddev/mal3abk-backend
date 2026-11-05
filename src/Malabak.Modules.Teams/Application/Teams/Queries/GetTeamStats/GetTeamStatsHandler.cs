using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetTeamStats;

public sealed class GetTeamStatsHandler : IQueryHandler<GetTeamStatsQuery, Result<TeamStatsDto>>
{
    private readonly ITeamsDbContext _dbContext;

    public GetTeamStatsHandler(ITeamsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<TeamStatsDto>> HandleAsync(GetTeamStatsQuery query, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == query.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure<TeamStatsDto>(TeamErrors.NotFound);
        }

        var rank = 1 + await _dbContext.Teams
            .CountAsync(t => t.Rating > team.Rating && !t.IsDeleted, cancellationToken);

        return Result.Success(TeamMapper.ToStatsDto(team, rank));
    }
}
