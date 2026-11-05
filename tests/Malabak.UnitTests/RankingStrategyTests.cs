using FluentAssertions;
using Malabak.Modules.Matches.Application.Matches.Events;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Domain.Events;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Teams.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Ranking;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class RankingStrategyTests
{
    private readonly IClock _clock = new FakeClock(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc));

    private static TeamsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TeamsDbContext>()
            .UseInMemoryDatabase(databaseName: $"Teams_Ranking_Tests_{Guid.NewGuid()}")
            .Options;

        return new TeamsDbContext(options);
    }

    [Fact]
    public void Elo_WhenEqualRating_HomeWin_ShouldAward3PointsAndIncreaseRating()
    {
        // Arrange
        var strategy = new EloRankingStrategy(kFactor: 32.0);
        var home = new TeamRatingSnapshot(Guid.NewGuid(), 1500.0);
        var away = new TeamRatingSnapshot(Guid.NewGuid(), 1500.0);

        // Act
        var result = strategy.CalculateMatch(home, away, homeScore: 2, awayScore: 1);

        // Assert
        result.Home.IsWin.Should().BeTrue();
        result.Home.Points.Should().Be(3);
        result.Home.GoalsFor.Should().Be(2);
        result.Home.GoalsAgainst.Should().Be(1);
        result.Home.GoalDifference.Should().Be(1);
        result.Home.RatingDelta.Should().Be(16.0); // 32 * (1.0 - 0.5)
        result.Home.NewRating.Should().Be(1516.0);

        result.Away.IsLoss.Should().BeTrue();
        result.Away.Points.Should().Be(0);
        result.Away.GoalsFor.Should().Be(1);
        result.Away.GoalsAgainst.Should().Be(2);
        result.Away.GoalDifference.Should().Be(-1);
        result.Away.RatingDelta.Should().Be(-16.0); // 32 * (0.0 - 0.5)
        result.Away.NewRating.Should().Be(1484.0);
    }

    [Fact]
    public void Elo_WhenEqualRating_Draw_ShouldAward1PointEachAndKeepRatingsIdentical()
    {
        // Arrange
        var strategy = new EloRankingStrategy(kFactor: 32.0);
        var home = new TeamRatingSnapshot(Guid.NewGuid(), 1500.0);
        var away = new TeamRatingSnapshot(Guid.NewGuid(), 1500.0);

        // Act
        var result = strategy.CalculateMatch(home, away, homeScore: 1, awayScore: 1);

        // Assert
        result.Home.IsDraw.Should().BeTrue();
        result.Home.Points.Should().Be(1);
        result.Home.RatingDelta.Should().Be(0.0);
        result.Home.NewRating.Should().Be(1500.0);

        result.Away.IsDraw.Should().BeTrue();
        result.Away.Points.Should().Be(1);
        result.Away.RatingDelta.Should().Be(0.0);
        result.Away.NewRating.Should().Be(1500.0);
    }

    [Fact]
    public void Elo_WhenUnderdogWins_ShouldYieldLargerRatingDeltaThanFavoriteWin()
    {
        // Arrange
        var strategy = new EloRankingStrategy(kFactor: 32.0);
        var underdog = new TeamRatingSnapshot(Guid.NewGuid(), 1300.0);
        var favorite = new TeamRatingSnapshot(Guid.NewGuid(), 1700.0);

        // Act: Underdog beats favorite
        var upsetResult = strategy.CalculateMatch(underdog, favorite, homeScore: 1, awayScore: 0);

        // Act: Favorite beats underdog
        var favoriteWinResult = strategy.CalculateMatch(favorite, underdog, homeScore: 1, awayScore: 0);

        // Assert: Underdog win yields much larger gain than normal favorite win
        upsetResult.Home.RatingDelta.Should().BeGreaterThan(favoriteWinResult.Home.RatingDelta);
        upsetResult.Home.RatingDelta.Should().BeGreaterThan(25.0); // Around ~29
        favoriteWinResult.Home.RatingDelta.Should().BeLessThan(5.0); // Around ~3
    }

    [Fact]
    public void Glicko2_WhenHomeWins_ShouldIncreaseRatingAndReduceUncertainty()
    {
        // Arrange
        var strategy = new Glicko2RankingStrategy();
        var home = new TeamRatingSnapshot(Guid.NewGuid(), Rating: 1500.0, RatingDeviation: 350.0, Volatility: 0.06);
        var away = new TeamRatingSnapshot(Guid.NewGuid(), Rating: 1500.0, RatingDeviation: 350.0, Volatility: 0.06);

        // Act
        var result = strategy.CalculateMatch(home, away, homeScore: 3, awayScore: 0);

        // Assert
        result.Home.IsWin.Should().BeTrue();
        result.Home.Points.Should().Be(3);
        result.Home.GoalDifference.Should().Be(3);
        result.Home.NewRating.Should().BeGreaterThan(1500.0);
        result.Home.NewRatingDeviation.Should().BeLessThan(350.0); // Uncertainty reduced after match

        result.Away.IsLoss.Should().BeTrue();
        result.Away.Points.Should().Be(0);
        result.Away.NewRating.Should().BeLessThan(1500.0);
        result.Away.NewRatingDeviation.Should().BeLessThan(350.0);
    }

    [Fact]
    public void Glicko2_InactivityDecay_ShouldOnlyApplyAfter30Days()
    {
        // Arrange
        var strategy = new Glicko2RankingStrategy();
        var rd = 150.0;
        var vol = 0.06;

        // Act
        var rd20Days = strategy.CalculateInactivityDecay(rd, vol, daysInactive: 20);
        var rd45Days = strategy.CalculateInactivityDecay(rd, vol, daysInactive: 45);

        // Assert
        rd20Days.Should().Be(150.0);
        rd45Days.Should().BeGreaterThan(150.0);
        rd45Days.Should().BeLessThanOrEqualTo(350.0);
    }

    [Fact]
    public async Task TeamRankingRecalculator_ShouldReplayConfirmedMatchesChronologicallyAndProduceConsistentStatsAsync()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var teamA = Team.Create(Guid.NewGuid(), "Team A", "team-a", "Cairo", "Cairo", Guid.NewGuid(), "INV101", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Team B", "team-b", "Cairo", "Cairo", Guid.NewGuid(), "INV102", _clock.UtcNow).Value;
        var teamC = Team.Create(Guid.NewGuid(), "Team C", "team-c", "Cairo", "Cairo", Guid.NewGuid(), "INV103", _clock.UtcNow).Value;

        db.Teams.AddRange(teamA, teamB, teamC);
        await db.SaveChangesAsync();

        var strategy = new EloRankingStrategy(kFactor: 32.0);
        var recalculator = new TeamRankingRecalculator(db, strategy, _clock, NullLogger<TeamRankingRecalculator>.Instance);

        var matches = new List<ConfirmedMatchRecord>
        {
            // Match 1: Team A 2 - 1 Team B
            new(Guid.NewGuid(), teamA.Id, teamB.Id, 2, 1, _clock.UtcNow.AddDays(-2)),
            // Match 2: Team B 3 - 0 Team C
            new(Guid.NewGuid(), teamB.Id, teamC.Id, 3, 0, _clock.UtcNow.AddDays(-1)),
            // Match 3: Team A 1 - 1 Team C
            new(Guid.NewGuid(), teamA.Id, teamC.Id, 1, 1, _clock.UtcNow)
        };

        // Act
        var result = await recalculator.RecalculateAllAsync(matches);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updatedA = await db.Teams.FirstAsync(t => t.Id == teamA.Id);
        var updatedB = await db.Teams.FirstAsync(t => t.Id == teamB.Id);
        var updatedC = await db.Teams.FirstAsync(t => t.Id == teamC.Id);

        // Team A: 1 Win, 1 Draw, 0 Loss -> 4 Points, GF: 3, GA: 2
        updatedA.MatchesPlayed.Should().Be(2);
        updatedA.Wins.Should().Be(1);
        updatedA.Draws.Should().Be(1);
        updatedA.Losses.Should().Be(0);
        updatedA.Points.Should().Be(4);
        updatedA.GoalsFor.Should().Be(3);
        updatedA.GoalsAgainst.Should().Be(2);
        updatedA.GoalDifference.Should().Be(1);

        // Team B: 1 Win, 0 Draw, 1 Loss -> 3 Points, GF: 4, GA: 2
        updatedB.MatchesPlayed.Should().Be(2);
        updatedB.Wins.Should().Be(1);
        updatedB.Draws.Should().Be(0);
        updatedB.Losses.Should().Be(1);
        updatedB.Points.Should().Be(3);
        updatedB.GoalsFor.Should().Be(4);
        updatedB.GoalsAgainst.Should().Be(2);
        updatedB.GoalDifference.Should().Be(2);

        // Team C: 0 Win, 1 Draw, 1 Loss -> 1 Point, GF: 1, GA: 4
        updatedC.MatchesPlayed.Should().Be(2);
        updatedC.Wins.Should().Be(0);
        updatedC.Draws.Should().Be(1);
        updatedC.Losses.Should().Be(1);
        updatedC.Points.Should().Be(1);
        updatedC.GoalsFor.Should().Be(1);
        updatedC.GoalsAgainst.Should().Be(4);
        updatedC.GoalDifference.Should().Be(-3);
    }

    [Fact]
    public async Task MatchResultConfirmedDomainEventHandler_ShouldTriggerMatchResultRecorderAsync()
    {
        // Arrange
        var fakeRecorder = new FakeTeamMatchResultRecorder();
        var handler = new MatchResultConfirmedDomainEventHandler(fakeRecorder, NullLogger<MatchResultConfirmedDomainEventHandler>.Instance);

        var matchId = Guid.NewGuid();
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();

        var evt = new MatchResultConfirmedDomainEvent(
            matchId,
            homeTeamId,
            awayTeamId,
            HomeScore: 3,
            AwayScore: 2,
            Outcome: MatchOutcome.HomeWin);

        // Act
        await handler.HandleAsync(evt, CancellationToken.None);

        // Assert
        fakeRecorder.RecordedCalls.Should().ContainSingle();
        var call = fakeRecorder.RecordedCalls[0];
        call.HomeTeamId.Should().Be(homeTeamId);
        call.AwayTeamId.Should().Be(awayTeamId);
        call.HomeScore.Should().Be(3);
        call.AwayScore.Should().Be(2);
    }

    private sealed class FakeTeamMatchResultRecorder : ITeamMatchResultRecorder
    {
        public List<(Guid HomeTeamId, Guid AwayTeamId, int HomeScore, int AwayScore)> RecordedCalls { get; } = [];

        public Task RecordConfirmedResultAsync(
            Guid homeTeamId,
            Guid awayTeamId,
            int homeScore,
            int awayScore,
            CancellationToken cancellationToken = default)
        {
            RecordedCalls.Add((homeTeamId, awayTeamId, homeScore, awayScore));
            return Task.CompletedTask;
        }
    }
}
