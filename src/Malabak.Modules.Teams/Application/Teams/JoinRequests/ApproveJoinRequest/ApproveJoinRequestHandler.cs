using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.JoinRequests.ApproveJoinRequest;

public sealed class ApproveJoinRequestHandler : ICommandHandler<ApproveJoinRequestCommand, Result>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public ApproveJoinRequestHandler(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(ApproveJoinRequestCommand command, CancellationToken cancellationToken = default)
    {
        // The requester is needed up front to take their lock before anything is counted.
        var requesterId = await _dbContext.TeamJoinRequests
            .Where(r => r.Id == command.RequestId && r.TeamId == command.TeamId)
            .Select(r => (Guid?)r.PlayerId)
            .FirstOrDefaultAsync(cancellationToken);

        await using var transaction = await TeamConcurrency.BeginAsync(_dbContext, cancellationToken);
        if (requesterId is not null)
        {
            await TeamConcurrency.LockPlayerAsync(_dbContext, requesterId.Value, cancellationToken);
        }

        await TeamConcurrency.LockTeamAsync(_dbContext, command.TeamId, cancellationToken);

        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .Include(t => t.JoinRequests)
            .FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure(TeamErrors.NotFound);
        }

        var requesterActiveTeams = requesterId is null
            ? 0
            : await _dbContext.TeamMembers.CountAsync(m => m.PlayerId == requesterId.Value && m.IsActive, cancellationToken);

        var nowUtc = _clock.UtcNow;
        var result = team.ApproveJoinRequest(command.RequestId, command.ReviewerPlayerId, nowUtc, requesterActiveTeams);
        if (!result.IsSuccess)
        {
            return Result.Failure(result.Error);
        }

        var member = result.Value;
        if (_dbContext.TeamMembers.Entry(member).State == EntityState.Detached)
        {
            _dbContext.TeamMembers.Add(member);
        }

        // A leader approved this player, which also lifts an earlier removal for the shared code.
        await TeamRejoin.AllowAsync(_dbContext, team.Id, member.PlayerId, nowUtc, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Result.Success();
    }
}
