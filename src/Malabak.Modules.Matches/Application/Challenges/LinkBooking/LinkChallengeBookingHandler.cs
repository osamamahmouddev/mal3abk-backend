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
    private readonly IBookingPaymentDetailsProvider? _bookingDetailsProvider;

    public LinkChallengeBookingHandler(
        IMatchesDbContext dbContext,
        ITeamCaptaincyChecker captaincyChecker,
        IClock clock,
        IBookingPaymentDetailsProvider? bookingDetailsProvider = null)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
        _clock = clock;
        _bookingDetailsProvider = bookingDetailsProvider;
    }

    public async Task<Result<TeamChallengeDto>> HandleAsync(LinkChallengeBookingCommand command, CancellationToken cancellationToken = default)
    {
        var isCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(command.TeamId, command.CallerPlayerId, cancellationToken);
        if (!isCaptain)
        {
            return Result.Failure<TeamChallengeDto>(ChallengeErrors.NotAuthorizedCaptain);
        }

        if (_bookingDetailsProvider != null)
        {
            var bookingCheck = await _bookingDetailsProvider.GetPayableBookingAsync(command.BookingId, command.CallerPlayerId, cancellationToken);
            if (!bookingCheck.IsSuccess)
            {
                return Result.Failure<TeamChallengeDto>(Error.Validation("Challenge.InvalidBooking", "The specified booking was not found or does not belong to you."));
            }
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

        var associatedMatch = await _dbContext.Matches
            .FirstOrDefaultAsync(m => m.ChallengeId == challenge.Id, cancellationToken);
        if (associatedMatch is not null)
        {
            associatedMatch.LinkBooking(command.BookingId, command.TeamId, _clock.UtcNow);
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
