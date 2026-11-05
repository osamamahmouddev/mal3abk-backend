namespace Malabak.SharedKernel.Ranking;

public sealed class Glicko2RankingStrategy : IRankingStrategy
{
    public const string Name = "Glicko2";
    public const double DefaultTau = 0.5;
    public const double DefaultConvergenceTolerance = 1e-6;
    private const double _scaleMultiplier = 173.7178;
    private const double _defaultRating = 1500.0;
    private const double _defaultVolatility = 0.06;
    private const double _minRatingDeviation = 30.0;
    private const double _maxRatingDeviation = 350.0;

    private readonly double _tau;
    private readonly double _tolerance;

    public string StrategyName => Name;

    public Glicko2RankingStrategy(double tau = DefaultTau, double tolerance = DefaultConvergenceTolerance)
    {
        _tau = tau > 0 ? tau : DefaultTau;
        _tolerance = tolerance > 0 ? tolerance : DefaultConvergenceTolerance;
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

        var homeActual = CalculateActualScore(isHomeWin, isDraw);
        var awayActual = CalculateActualScore(isAwayWin, isDraw);

        var (homeNewRating, homeNewRd, homeNewVol) = CalculateGlicko2Update(
            playerRating: homeTeam.Rating,
            playerRd: homeTeam.RatingDeviation,
            playerVol: homeTeam.Volatility,
            opponentRating: awayTeam.Rating,
            opponentRd: awayTeam.RatingDeviation,
            actualScore: homeActual);

        var (awayNewRating, awayNewRd, awayNewVol) = CalculateGlicko2Update(
            playerRating: awayTeam.Rating,
            playerRd: awayTeam.RatingDeviation,
            playerVol: awayTeam.Volatility,
            opponentRating: homeTeam.Rating,
            opponentRd: homeTeam.RatingDeviation,
            actualScore: awayActual);

        var homeDelta = Math.Round(homeNewRating - homeTeam.Rating, 2);
        var awayDelta = Math.Round(awayNewRating - awayTeam.Rating, 2);

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
            NewRatingDeviation: homeNewRd,
            OldVolatility: homeTeam.Volatility,
            NewVolatility: homeNewVol);

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
            NewRatingDeviation: awayNewRd,
            OldVolatility: awayTeam.Volatility,
            NewVolatility: awayNewVol);

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

        // ADR 0008: Inactive teams experience increase in Rating Deviation (uncertainty)
        var inactiveDaysPastThreshold = daysInactive - 30;
        var decayConstant = Math.Max(0.01, volatility) * 15.0;
        var decayedRd = Math.Sqrt(Math.Pow(currentRatingDeviation, 2) + (Math.Pow(decayConstant, 2) * inactiveDaysPastThreshold));

        return Math.Min(_maxRatingDeviation, Math.Round(decayedRd, 2));
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

    private (double NewRating, double NewRd, double NewVol) CalculateGlicko2Update(
        double playerRating,
        double playerRd,
        double playerVol,
        double opponentRating,
        double opponentRd,
        double actualScore)
    {
        // 1. Convert to Glicko-2 scale
        var r = (playerRating - _defaultRating) / _scaleMultiplier;
        var rd = Math.Clamp(playerRd, _minRatingDeviation, _maxRatingDeviation) / _scaleMultiplier;
        var sigma = playerVol > 0 ? playerVol : _defaultVolatility;

        var rOpp = (opponentRating - _defaultRating) / _scaleMultiplier;
        var rdOpp = Math.Clamp(opponentRd, _minRatingDeviation, _maxRatingDeviation) / _scaleMultiplier;

        // 2. Compute g and E
        var gOpp = G(rdOpp);
        var expected = E(r, rOpp, rdOpp);

        // 3. Compute variance v
        var v = 1.0 / (Math.Pow(gOpp, 2) * expected * (1.0 - expected));

        // 4. Compute delta
        var delta = v * gOpp * (actualScore - expected);

        // 5. Determine new volatility sigma' using Illinois algorithm
        var a = Math.Log(Math.Pow(sigma, 2));
        double f(double x)
        {
            var expX = Math.Exp(x);
            var rdSqPlusVPlusExpX = Math.Pow(rd, 2) + v + expX;
            var term1 = expX * (Math.Pow(delta, 2) - Math.Pow(rd, 2) - v - expX) / (2.0 * Math.Pow(rdSqPlusVPlusExpX, 2));
            var term2 = (x - a) / Math.Pow(_tau, 2);
            return term1 - term2;
        }

        var bigA = a;
        double bigB;

        if (Math.Pow(delta, 2) > Math.Pow(rd, 2) + v)
        {
            bigB = Math.Log(Math.Pow(delta, 2) - Math.Pow(rd, 2) - v);
        }
        else
        {
            var k = 1;
            while (f(a - (k * _tau)) < 0)
            {
                k++;
            }
            bigB = a - (k * _tau);
        }

        var fA = f(bigA);
        var fB = f(bigB);

        while (Math.Abs(bigB - bigA) > _tolerance)
        {
            var bigC = bigA + ((bigA - bigB) * fA / (fB - fA));
            var fC = f(bigC);

            if (fC * fB <= 0)
            {
                bigA = bigB;
                fA = fB;
            }
            else
            {
                fA /= 2.0;
            }

            bigB = bigC;
            fB = fC;
        }

        var newSigma = Math.Exp(bigA / 2.0);

        // 6. Update rating deviation to pre-rating period value
        var rdStar = Math.Sqrt(Math.Pow(rd, 2) + Math.Pow(newSigma, 2));

        // 7. Update rating and rating deviation
        var newRdGlicko2 = 1.0 / Math.Sqrt((1.0 / Math.Pow(rdStar, 2)) + (1.0 / v));
        var newRGlicko2 = r + (Math.Pow(newRdGlicko2, 2) * gOpp * (actualScore - expected));

        // 8. Convert back to original scale
        var finalRating = Math.Round((newRGlicko2 * _scaleMultiplier) + _defaultRating, 2);
        var finalRd = Math.Clamp(Math.Round(newRdGlicko2 * _scaleMultiplier, 2), _minRatingDeviation, _maxRatingDeviation);
        var finalVol = Math.Round(newSigma, 6);

        return (finalRating, finalRd, finalVol);
    }

    private static double G(double rd)
    {
        return 1.0 / Math.Sqrt(1.0 + (3.0 * Math.Pow(rd, 2) / Math.Pow(Math.PI, 2)));
    }

    private static double E(double r, double rOpp, double rdOpp)
    {
        return 1.0 / (1.0 + Math.Exp(-G(rdOpp) * (r - rOpp)));
    }
}
