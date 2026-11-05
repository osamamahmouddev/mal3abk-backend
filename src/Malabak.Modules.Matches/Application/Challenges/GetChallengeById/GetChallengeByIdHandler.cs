using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Challenges.GetChallengeById;

public sealed class GetChallengeByIdHandler : IQueryHandler<GetChallengeByIdQuery, Result<TeamChallengeDto>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;

    public GetChallengeByIdHandler(
        IMatchesDbContext dbContext,
        ITeamCaptaincyChecker captaincyChecker)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
    }

    public async Task<Result<TeamChallengeDto>> HandleAsync(GetChallengeByIdQuery query, CancellationToken cancellationToken = default)
    {
        var challenge = await _dbContext.TeamChallenges
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.ChallengeId, cancellationToken);

        if (challenge is null)
        {
            return Result.Failure<TeamChallengeDto>(ChallengeErrors.NotFound);
        }

        var isChallengerCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(challenge.ChallengerTeamId, query.CallerPlayerId, cancellationToken);
        var isChallengedCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(challenge.ChallengedTeamId, query.CallerPlayerId, cancellationToken);

        if (!isChallengerCaptain && !isChallengedCaptain)
        {
            return Result.Failure<TeamChallengeDto>(ChallengeErrors.NotAuthorizedCaptain);
        }

        var challengerName = await _captaincyChecker.GetTeamNameAsync(challenge.ChallengerTeamId, cancellationToken) ?? "Challenger Team";
        var challengedName = await _captaincyChecker.GetTeamNameAsync(challenge.ChallengedTeamId, cancellationToken) ?? "Challenged Team";

        var callerTeamId = isChallengerCaptain ? challenge.ChallengerTeamId : challenge.ChallengedTeamId;

        return Result.Success(new TeamChallengeDto(
            challenge.Id,
            challenge.ChallengerTeamId,
            challengerName,
            challenge.ChallengedTeamId,
            challengedName,
            challenge.ChallengerCaptainId,
            challenge.ProposedDateUtc,
            challenge.ProposedDurationMinutes,
            challenge.ProposedPitchId,
            challenge.ProposedVenueId,
            challenge.ProposedVenueName,
            challenge.Notes,
            challenge.Status,
            challenge.CurrentTurnTeamId,
            IsMyTurn: challenge.CurrentTurnTeamId == callerTeamId,
            challenge.AcceptedAtUtc,
            challenge.DeclinedAtUtc,
            challenge.DeclinedReason,
            challenge.MatchId,
            challenge.CreatedAtUtc));
    }
}
