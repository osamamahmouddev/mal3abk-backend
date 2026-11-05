using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Challenges.GetTeamChallenges;

public sealed class GetTeamChallengesHandler : IQueryHandler<GetTeamChallengesQuery, Result<IReadOnlyList<TeamChallengeDto>>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;

    public GetTeamChallengesHandler(
        IMatchesDbContext dbContext,
        ITeamCaptaincyChecker captaincyChecker)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
    }

    public async Task<Result<IReadOnlyList<TeamChallengeDto>>> HandleAsync(GetTeamChallengesQuery query, CancellationToken cancellationToken = default)
    {
        var isCaptain = await _captaincyChecker.IsCaptainOrViceCaptainAsync(query.TeamId, query.CallerPlayerId, cancellationToken);
        if (!isCaptain)
        {
            return Result.Failure<IReadOnlyList<TeamChallengeDto>>(ChallengeErrors.NotAuthorizedCaptain);
        }

        var challengesQuery = _dbContext.TeamChallenges
            .AsNoTracking()
            .Where(c => c.ChallengerTeamId == query.TeamId || c.ChallengedTeamId == query.TeamId);

        if (query.Status.HasValue)
        {
            challengesQuery = challengesQuery.Where(c => c.Status == query.Status.Value);
        }

        var challenges = await challengesQuery
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var teamName = await _captaincyChecker.GetTeamNameAsync(query.TeamId, cancellationToken) ?? "My Team";

        var dtos = new List<TeamChallengeDto>(challenges.Count);
        foreach (var challenge in challenges)
        {
            var isChallenger = challenge.ChallengerTeamId == query.TeamId;
            var otherTeamId = isChallenger ? challenge.ChallengedTeamId : challenge.ChallengerTeamId;
            var otherTeamName = await _captaincyChecker.GetTeamNameAsync(otherTeamId, cancellationToken) ?? "Opponent Team";

            dtos.Add(new TeamChallengeDto(
                challenge.Id,
                challenge.ChallengerTeamId,
                isChallenger ? teamName : otherTeamName,
                challenge.ChallengedTeamId,
                isChallenger ? otherTeamName : teamName,
                challenge.ChallengerCaptainId,
                challenge.ProposedDateUtc,
                challenge.ProposedDurationMinutes,
                challenge.ProposedPitchId,
                challenge.ProposedVenueId,
                challenge.ProposedVenueName,
                challenge.Notes,
                challenge.Status,
                challenge.CurrentTurnTeamId,
                IsMyTurn: challenge.CurrentTurnTeamId == query.TeamId,
                challenge.AcceptedAtUtc,
                challenge.DeclinedAtUtc,
                challenge.DeclinedReason,
                challenge.MatchId,
                challenge.CreatedAtUtc));
        }

        return Result.Success<IReadOnlyList<TeamChallengeDto>>(dtos);
    }
}
