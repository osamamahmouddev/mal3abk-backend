using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Members.RemoveTeamMember;

public sealed class RemoveTeamMemberHandler : ICommandHandler<RemoveTeamMemberCommand, Result>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public RemoveTeamMemberHandler(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(RemoveTeamMemberCommand command, CancellationToken cancellationToken = default)
    {
        await using var transaction = await TeamConcurrency.BeginAsync(_dbContext, cancellationToken);
        await TeamConcurrency.LockPlayerAsync(_dbContext, command.MemberPlayerId, cancellationToken);
        await TeamConcurrency.LockTeamAsync(_dbContext, command.TeamId, cancellationToken);

        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure(TeamErrors.NotFound);
        }

        var removeResult = team.RemoveMember(command.MemberPlayerId, command.RequestingPlayerId, _clock.UtcNow);
        if (!removeResult.IsSuccess)
        {
            return removeResult;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Result.Success();
    }
}
