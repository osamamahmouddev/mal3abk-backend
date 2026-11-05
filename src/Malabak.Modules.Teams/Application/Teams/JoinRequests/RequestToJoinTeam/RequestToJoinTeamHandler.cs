using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.JoinRequests.RequestToJoinTeam;

public sealed class RequestToJoinTeamHandler : ICommandHandler<RequestToJoinTeamCommand, Result<TeamJoinRequestDto>>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public RequestToJoinTeamHandler(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<TeamJoinRequestDto>> HandleAsync(RequestToJoinTeamCommand command, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .Include(t => t.JoinRequests)
            .FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure<TeamJoinRequestDto>(TeamErrors.NotFound);
        }

        var activeTeamsCount = await _dbContext.TeamMembers
            .CountAsync(m => m.PlayerId == command.PlayerId && m.IsActive, cancellationToken);

        var result = team.CreateJoinRequest(command.PlayerId, command.Message, activeTeamsCount, _clock.UtcNow);
        if (!result.IsSuccess)
        {
            return Result.Failure<TeamJoinRequestDto>(result.Error);
        }

        var request = result.Value;
        if (_dbContext.TeamJoinRequests.Entry(request).State == EntityState.Detached)
        {
            _dbContext.TeamJoinRequests.Add(request);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new TeamJoinRequestDto(
            request.Id,
            request.TeamId,
            request.PlayerId,
            request.Message,
            request.Status,
            request.CreatedAtUtc,
            request.ReviewedAtUtc,
            request.ReviewedByPlayerId));
    }
}
