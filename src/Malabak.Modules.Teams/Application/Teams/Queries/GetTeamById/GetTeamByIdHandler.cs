using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetTeamById;

public sealed class GetTeamByIdHandler : IQueryHandler<GetTeamByIdQuery, Result<TeamDetailDto>>
{
    private readonly ITeamsDbContext _dbContext;

    public GetTeamByIdHandler(ITeamsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<TeamDetailDto>> HandleAsync(GetTeamByIdQuery query, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == query.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure<TeamDetailDto>(TeamErrors.NotFound);
        }

        var isCallerMember = query.RequestingPlayerId.HasValue &&
            team.Members.Any(m => m.PlayerId == query.RequestingPlayerId.Value && m.IsActive);

        return Result.Success(TeamMapper.ToDetailDto(team, includePrivateInfo: isCallerMember));
    }
}
