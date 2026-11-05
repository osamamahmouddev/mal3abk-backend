namespace Malabak.SharedKernel.Ranking;

public sealed class EloRankingStrategy : IRankingStrategy
{
    public const string Name = "Elo";
    public const double DefaultKFactor = 32.0;

    private readonly double _kFactor;

    public string StrategyName => Name;

    public EloRankingStrategy(double kFactor = DefaultKFactor)
    {
        _kFactor = kFactor > 0 ? kFactor : DefaultKFactor;
    }

    public MatchCalculationOutcome CalculateMatch(
        TeamRatingSnapshot homeTeam,
        TeamRatingSnapshot awayTeam,
        int homeScore,
        int awayScore)
    {
        ArgumentNullException.ThrowIfNull(homeTeam);
        ArgumentNullException.ThrowIfNull(awayTeam);

        var isHomeWin = homeScore > awayScore;
        var isDraw = homeScore == awayScore;
        var isAwayWin = homeScore < awayScore;

        var actualHomeScore = CalculateActualScore(isHomeWin, isDraw);
        var actualAwayScore = CalculateActualScore(isAwayWin, isDraw);

        var expectedHomeScore = 1.0 / (1.0 + Math.Pow(10.0, (awayTeam.Rating - homeTeam.Rating) / 400.0));
        var expectedAwayScore = 1.0 - expectedHomeScore;

        var homeDelta = Math.Round(_kFactor * (actualHomeScore - expectedHomeScore), 2);
        var awayDelta = Math.Round(_kFactor * (actualAwayScore - expectedAwayScore), 2);

        var homeNewRating = Math.Round(homeTeam.Rating + homeDelta, 2);
        var awayNewRating = Math.Round(awayTeam.Rating + awayDelta, 2);

        var homePoints = CalculatePoints(isHomeWin, isDraw);
        var awayPoints = CalculatePoints(isAwayWin, isDraw);

        var homeResult = new MatchRankingResult(
            TeamId: homeTeam.TeamId,
            IsWin: isHomeWin,
            IsDraw: isDraw,
            IsLoss: isAwayWin,
            Points: homePoints,
            GoalsFor: homeScore,
            GoalsAgainst: awayScore,
            GoalDifference: homeScore - awayScore,
            OldRating: homeTeam.Rating,
            NewRating: homeNewRating,
            RatingDelta: homeDelta,
            OldRatingDeviation: homeTeam.RatingDeviation,
            NewRatingDeviation: homeTeam.RatingDeviation,
            OldVolatility: homeTeam.Volatility,
            NewVolatility: homeTeam.Volatility);

        var awayResult = new MatchRankingResult(
            TeamId: awayTeam.TeamId,
            IsWin: isAwayWin,
            IsDraw: isDraw,
            IsLoss: isHomeWin,
            Points: awayPoints,
            GoalsFor: awayScore,
            GoalsAgainst: homeScore,
            GoalDifference: awayScore - homeScore,
            OldRating: awayTeam.Rating,
            NewRating: awayNewRating,
            RatingDelta: awayDelta,
            OldRatingDeviation: awayTeam.RatingDeviation,
            NewRatingDeviation: awayTeam.RatingDeviation,
            OldVolatility: awayTeam.Volatility,
            NewVolatility: awayTeam.Volatility);

        return new MatchCalculationOutcome(homeResult, awayResult);
    }

    public double CalculateInactivityDecay(
        double currentRatingDeviation,
        double volatility,
        int daysInactive)
    {
        if (daysInactive <= 30)
        {
            return currentRatingDeviation;
        }

        // Slight decay of confidence (up to max 350.0) for inactive teams
        var extraDays = daysInactive - 30;
        var decayFactor = Math.Sqrt(extraDays * 1.5);
        return Math.Min(350.0, Math.Round(currentRatingDeviation + decayFactor, 2));
    }

    private static double CalculateActualScore(bool isWin, bool isDraw)
    {
        if (isWin)
        {
            return 1.0;
        }

        return isDraw ? 0.5 : 0.0;
    }

    private static int CalculatePoints(bool isWin, bool isDraw)
    {
        if (isWin)
        {
            return 3;
        }

        return isDraw ? 1 : 0;
    }
}
