using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Matches.CancelMatch;

public sealed class CancelMatchHandler : ICommandHandler<CancelMatchCommand, Result>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;
    private readonly IClock _clock;

    public CancelMatchHandler(
        IMatchesDbContext dbContext,
        ITeamCaptaincyChecker captaincyChecker,
        IClock clock)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(CancelMatchCommand command, CancellationToken cancellationToken = default)
    {
        var isCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(command.CallerTeamId, command.CallerPlayerId, cancellationToken);
        if (!isCaptain)
        {
            return Result.Failure(MatchErrors.NotAuthorizedCaptain);
        }

        var match = await _dbContext.Matches
            .FirstOrDefaultAsync(m => m.Id == command.MatchId, cancellationToken);

        if (match is null)
        {
            return Result.Failure(MatchErrors.NotFound);
        }

        var cancelResult = match.Cancel(command.CallerTeamId, command.Reason, _clock.UtcNow);
        if (!cancelResult.IsSuccess)
        {
            return cancelResult;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
