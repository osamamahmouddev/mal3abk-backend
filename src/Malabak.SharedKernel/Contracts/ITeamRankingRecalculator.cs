namespace Malabak.SharedKernel.Contracts;

public sealed record ConfirmedMatchRecord(
    Guid MatchId,
    Guid HomeTeamId,
    Guid AwayTeamId,
    int HomeScore,
    int AwayScore,
    DateTime CompletedAtUtc);

public interface ITeamRankingRecalculator
{
    Task<Result> RecalculateAllAsync(
        IReadOnlyList<ConfirmedMatchRecord> confirmedMatches,
        CancellationToken cancellationToken = default);
}
