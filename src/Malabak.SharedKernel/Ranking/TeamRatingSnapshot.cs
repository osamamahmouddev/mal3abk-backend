namespace Malabak.SharedKernel.Ranking;

public sealed record TeamRatingSnapshot(
    Guid TeamId,
    double Rating,
    double RatingDeviation = 350.0,
    double Volatility = 0.06);
