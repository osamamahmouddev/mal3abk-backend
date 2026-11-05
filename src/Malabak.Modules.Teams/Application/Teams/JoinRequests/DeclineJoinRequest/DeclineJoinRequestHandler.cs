using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.JoinRequests.DeclineJoinRequest;

public sealed class DeclineJoinRequestHandler : ICommandHandler<DeclineJoinRequestCommand, Result>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public DeclineJoinRequestHandler(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(DeclineJoinRequestCommand command, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .Include(t => t.JoinRequests)
            .FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure(TeamErrors.NotFound);
        }

        var result = team.DeclineJoinRequest(command.RequestId, command.ReviewerPlayerId, _clock.UtcNow);
        if (!result.IsSuccess)
        {
            return Result.Failure(result.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
