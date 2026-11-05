using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Members.TransferCaptaincy;

public sealed class TransferCaptaincyHandler : ICommandHandler<TransferCaptaincyCommand, Result>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IClock _clock;

    public TransferCaptaincyHandler(ITeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(TransferCaptaincyCommand command, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == command.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure(TeamErrors.NotFound);
        }

        var transferResult = team.TransferCaptaincy(command.NewCaptainPlayerId, command.CurrentCaptainPlayerId, _clock.UtcNow);
        if (!transferResult.IsSuccess)
        {
            return transferResult;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
