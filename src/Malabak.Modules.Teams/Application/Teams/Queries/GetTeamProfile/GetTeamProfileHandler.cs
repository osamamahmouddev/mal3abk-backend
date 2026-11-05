using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetTeamProfile;

public sealed class GetTeamProfileHandler : IQueryHandler<GetTeamProfileQuery, Result<TeamProfileDto>>
{
    private readonly ITeamsDbContext _dbContext;

    public GetTeamProfileHandler(ITeamsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<TeamProfileDto>> HandleAsync(GetTeamProfileQuery query, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == query.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure<TeamProfileDto>(TeamErrors.NotFound);
        }

        var isCallerMember = query.RequestingPlayerId.HasValue &&
            team.Members.Any(m => m.PlayerId == query.RequestingPlayerId.Value && m.IsActive);

        // Privacy rule: private teams are hidden unless caller is a member
        if (!team.IsPublic && !isCallerMember)
        {
            return Result.Failure<TeamProfileDto>(TeamErrors.NotFound);
        }

        // Project rank based on team rating
        var rank = 1 + await _dbContext.Teams
            .CountAsync(t => t.Rating > team.Rating && !t.IsDeleted, cancellationToken);

        var profile = TeamMapper.ToProfileDto(team, rank, query.RequestingPlayerId);

        return Result.Success(profile);
    }
}
