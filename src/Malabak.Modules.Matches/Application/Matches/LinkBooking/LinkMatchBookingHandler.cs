using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Matches.LinkBooking;

public sealed class LinkMatchBookingHandler : ICommandHandler<LinkMatchBookingCommand, Result<MatchDto>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;
    private readonly IClock _clock;

    public LinkMatchBookingHandler(
        IMatchesDbContext dbContext,
        ITeamCaptaincyChecker captaincyChecker,
        IClock clock)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
        _clock = clock;
    }

    public async Task<Result<MatchDto>> HandleAsync(LinkMatchBookingCommand command, CancellationToken cancellationToken = default)
    {
        var isCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(command.CallerTeamId, command.CallerPlayerId, cancellationToken);
        if (!isCaptain)
        {
            return Result.Failure<MatchDto>(MatchErrors.NotAuthorizedCaptain);
        }

        var match = await _dbContext.Matches
            .FirstOrDefaultAsync(m => m.Id == command.MatchId, cancellationToken);

        if (match is null)
        {
            return Result.Failure<MatchDto>(MatchErrors.NotFound);
        }

        var linkResult = match.LinkBooking(command.BookingId, command.CallerTeamId, _clock.UtcNow);
        if (!linkResult.IsSuccess)
        {
            return Result.Failure<MatchDto>(linkResult.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var homeTeamName = await _captaincyChecker.GetTeamNameAsync(match.HomeTeamId, cancellationToken) ?? "Home Team";
        var awayTeamName = await _captaincyChecker.GetTeamNameAsync(match.AwayTeamId, cancellationToken) ?? "Away Team";

        return Result.Success(MatchMapper.ToDto(match, homeTeamName, awayTeamName));
    }
}
