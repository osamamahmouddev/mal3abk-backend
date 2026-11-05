using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Challenges.LinkBooking;

public sealed class LinkChallengeBookingHandler : ICommandHandler<LinkChallengeBookingCommand, Result<TeamChallengeDto>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;
    private readonly IClock _clock;

    public LinkChallengeBookingHandler(
        IMatchesDbContext dbContext,
        ITeamCaptaincyChecker captaincyChecker,
        IClock clock)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
        _clock = clock;
    }

    public async Task<Result<TeamChallengeDto>> HandleAsync(LinkChallengeBookingCommand command, CancellationToken cancellationToken = default)
    {
        var isCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(command.TeamId, command.CallerPlayerId, cancellationToken);
        if (!isCaptain)
        {
            return Result.Failure<TeamChallengeDto>(ChallengeErrors.NotAuthorizedCaptain);
        }

        var challenge = await _dbContext.TeamChallenges
            .FirstOrDefaultAsync(c => c.Id == command.ChallengeId, cancellationToken);

        if (challenge is null)
        {
            return Result.Failure<TeamChallengeDto>(ChallengeErrors.NotFound);
        }

        var linkResult = challenge.LinkBooking(command.BookingId, command.TeamId, _clock.UtcNow);
        if (!linkResult.IsSuccess)
        {
            return Result.Failure<TeamChallengeDto>(linkResult.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var challengerName = await _captaincyChecker.GetTeamNameAsync(challenge.ChallengerTeamId, cancellationToken);
        var challengedName = await _captaincyChecker.GetTeamNameAsync(challenge.ChallengedTeamId, cancellationToken);

        return Result.Success(ChallengeMapper.ToDto(
            challenge,
            challengerName,
            challengedName,
            command.TeamId));
    }
}
