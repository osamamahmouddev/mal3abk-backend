namespace Malabak.SharedKernel.Ranking;

public sealed record MatchRankingResult(
    Guid TeamId,
    bool IsWin,
    bool IsDraw,
    bool IsLoss,
    int Points,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    double OldRating,
    double NewRating,
    double RatingDelta,
    double OldRatingDeviation,
    double NewRatingDeviation,
    double OldVolatility,
    double NewVolatility);

public sealed record MatchCalculationOutcome(
    MatchRankingResult Home,
    MatchRankingResult Away);
