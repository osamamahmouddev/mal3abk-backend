namespace Malabak.SharedKernel.Ranking;

public interface IRankingStrategy
{
    string StrategyName { get; }

    MatchCalculationOutcome CalculateMatch(
        TeamRatingSnapshot homeTeam,
        TeamRatingSnapshot awayTeam,
        int homeScore,
        int awayScore);

    double CalculateInactivityDecay(
        double currentRatingDeviation,
        double volatility,
        int daysInactive);
}
