using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Members.LeaveTeam;

public sealed class LeaveTeamHandler : ICommandHandler<LeaveTeamCommand, Result>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public LeaveTeamHandler(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(LeaveTeamCommand command, CancellationToken cancellationToken = default)
    {
        // Serialised with joins on this team, so a lone captain cannot close it while someone is joining.
        await using var transaction = await TeamConcurrency.BeginAsync(_dbContext, cancellationToken);
        await TeamConcurrency.LockPlayerAsync(_dbContext, command.PlayerId, cancellationToken);
        await TeamConcurrency.LockTeamAsync(_dbContext, command.TeamId, cancellationToken);

        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .Include(t => t.JoinRequests)
            .FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure(TeamErrors.NotFound);
        }

        var result = team.LeaveTeam(command.PlayerId, _clock.UtcNow);
        if (!result.IsSuccess)
        {
            return Result.Failure(result.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Result.Success();
    }
}
