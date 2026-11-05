using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.JoinRequests.GetTeamJoinRequests;

public sealed class GetTeamJoinRequestsHandler : IQueryHandler<GetTeamJoinRequestsQuery, Result<IReadOnlyList<TeamJoinRequestDto>>>
{
    private readonly ITeamsDbContext _dbContext;

    public GetTeamJoinRequestsHandler(ITeamsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<TeamJoinRequestDto>>> HandleAsync(GetTeamJoinRequestsQuery query, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == query.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure<IReadOnlyList<TeamJoinRequestDto>>(TeamErrors.NotFound);
        }

        var caller = team.Members.FirstOrDefault(m => m.PlayerId == query.CallerPlayerId && m.IsActive);
        if (caller is null || (caller.Role != TeamRole.Captain && caller.Role != TeamRole.ViceCaptain))
        {
            return Result.Failure<IReadOnlyList<TeamJoinRequestDto>>(TeamErrors.NotCaptainOrViceCaptain);
        }

        var requestsQuery = _dbContext.TeamJoinRequests
            .AsNoTracking()
            .Where(r => r.TeamId == query.TeamId);

        if (query.Status.HasValue)
        {
            requestsQuery = requestsQuery.Where(r => r.Status == query.Status.Value);
        }

        var requests = await requestsQuery
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new TeamJoinRequestDto(
                r.Id,
                r.TeamId,
                r.PlayerId,
                r.Message,
                r.Status,
                r.CreatedAtUtc,
                r.ReviewedAtUtc,
                r.ReviewedByPlayerId))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<TeamJoinRequestDto>>(requests);
    }
}
