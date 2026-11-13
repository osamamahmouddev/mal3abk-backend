using FluentAssertions;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.Modules.Matches.Infrastructure.Jobs;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamLeaderboard;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Teams.Infrastructure.Services;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Ranking;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class Glicko2AndRankingEngineEdgeCaseTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc));

    private static TeamsDbContext CreateTeamsDb()
    {
        var options = new DbContextOptionsBuilder<TeamsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TeamsDbContext(options);
    }

    private static MatchesDbContext CreateMatchesDb()
    {
        var options = new DbContextOptionsBuilder<MatchesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MatchesDbContext(options);
    }

    [Fact]
    public void Glicko2_StartingDefaults_ShouldMatchADR0008Specifications()
    {
        // ADR-0008 §3:
        // Initial Rating: mu = 1500
        // Rating Deviation (Uncertainty): phi = 350
        // Volatility: sigma = 0.06
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Cairo United",
            slug: "cairo-united",
            city: "Cairo",
            governorate: "Cairo",
            captainId: Guid.NewGuid(),
            inviteCode: "CAIR01",
            nowUtc: _clock.UtcNow).Value;

        team.Rating.Should().Be(1500.0);
        team.RatingDeviation.Should().Be(350.0);
        team.RatingVolatility.Should().Be(0.06);
    }

    [Fact]
    public void Glicko2_WinAgainstHigherRankedTeam_ShouldAwardMoreRatingDeltaThanAgainstLowerRankedTeam()
    {
        // Arrange
        var strategy = new Glicko2RankingStrategy();
        var player = new TeamRatingSnapshot(Guid.NewGuid(), Rating: 1500.0, RatingDeviation: 100.0, Volatility: 0.06);

        var higherRankedOpponent = new TeamRatingSnapshot(Guid.NewGuid(), Rating: 1700.0, RatingDeviation: 100.0, Volatility: 0.06);
        var lowerRankedOpponent = new TeamRatingSnapshot(Guid.NewGuid(), Rating: 1300.0, RatingDeviation: 100.0, Volatility: 0.06);

        // Act
        var upsetWin = strategy.CalculateMatch(player, higherRankedOpponent, homeScore: 2, awayScore: 1);
        var expectedWin = strategy.CalculateMatch(player, lowerRankedOpponent, homeScore: 2, awayScore: 1);

        // Assert: Beating a stronger team yields more rating points than beating a weaker team
        upsetWin.Home.RatingDelta.Should().BeGreaterThan(expectedWin.Home.RatingDelta);
        upsetWin.Home.NewRating.Should().BeGreaterThan(expectedWin.Home.NewRating);
    }

    [Fact]
    public void Glicko2_OpponentWithHighRD_ContributesLessCertaintyThanLowRD()
    {
        // Arrange
        var strategy = new Glicko2RankingStrategy();
        var team = new TeamRatingSnapshot(Guid.NewGuid(), Rating: 1500.0, RatingDeviation: 200.0, Volatility: 0.06);

        // Opponent with very low uncertainty (established player, RD=50)
        var certainOpponent = new TeamRatingSnapshot(Guid.NewGuid(), Rating: 1500.0, RatingDeviation: 50.0, Volatility: 0.06);
        // Opponent with maximum uncertainty (brand new team, RD=350)
        var uncertainOpponent = new TeamRatingSnapshot(Guid.NewGuid(), Rating: 1500.0, RatingDeviation: 350.0, Volatility: 0.06);

        // Act
        var matchAgainstCertain = strategy.CalculateMatch(team, certainOpponent, homeScore: 1, awayScore: 0);
        var matchAgainstUncertain = strategy.CalculateMatch(team, uncertainOpponent, homeScore: 1, awayScore: 0);

        // Assert: A match against an opponent with high uncertainty provides less information/certainty,
        // so the team's rating delta against an uncertain opponent is smaller than against an established opponent
        matchAgainstCertain.Home.RatingDelta.Should().BeGreaterThan(matchAgainstUncertain.Home.RatingDelta);
    }

    [Fact]
    public void Glicko2_InactivityDecay_BoundaryAndCapRules()
    {
        // Arrange
        var strategy = new Glicko2RankingStrategy();
        var initialRd = 80.0;
        var vol = 0.06;

        // Act & Assert
        // <= 30 days: exactly zero decay
        strategy.CalculateInactivityDecay(initialRd, vol, daysInactive: 0).Should().Be(80.0);
        strategy.CalculateInactivityDecay(initialRd, vol, daysInactive: 15).Should().Be(80.0);
        strategy.CalculateInactivityDecay(initialRd, vol, daysInactive: 30).Should().Be(80.0);

        // 31 days: strictly positive decay
        var decayed31 = strategy.CalculateInactivityDecay(initialRd, vol, daysInactive: 31);
        decayed31.Should().BeGreaterThan(80.0);

        // 365 days: significant decay towards 350
        var decayedYear = strategy.CalculateInactivityDecay(initialRd, vol, daysInactive: 365);
        decayedYear.Should().BeGreaterThan(decayed31);
        decayedYear.Should().BeLessThanOrEqualTo(350.0);

        // Capped at 350.0
        var extremeDecay = strategy.CalculateInactivityDecay(349.0, vol, daysInactive: 1000);
        extremeDecay.Should().Be(350.0);
    }

    [Fact]
    public async Task RankingRecalculation_ExcludesDisputedAndExpiredMatches_AndIsIdempotentAsync()
    {
        // Arrange
        using var teamsDb = CreateTeamsDb();
        using var matchesDb = CreateMatchesDb();

        var teamA = Team.Create(Guid.NewGuid(), "Team Alpha", "alpha", "Cairo", "Cairo", Guid.NewGuid(), "ALP001", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Team Beta", "beta", "Cairo", "Cairo", Guid.NewGuid(), "BET001", _clock.UtcNow).Value;
        teamsDb.Teams.AddRange(teamA, teamB);
        await teamsDb.SaveChangesAsync();

        // 1. Confirmed completed match (should be counted)
        var confirmedMatch = Match.Schedule(
            Guid.NewGuid(), teamA.Id, teamB.Id, _clock.UtcNow.AddDays(-3), 60,
            Guid.NewGuid(), Guid.NewGuid(), "Venue 1", Guid.NewGuid(), null, _clock.UtcNow.AddDays(-4)).Value;
        confirmedMatch.SubmitResult(2, 0, teamA.Id, teamA.CaptainId, _clock.UtcNow.AddDays(-3).AddHours(1));
        confirmedMatch.VerifyResult(MatchVerificationAction.Confirm, teamB.Id, teamB.CaptainId, null, _clock.UtcNow.AddDays(-3).AddHours(2));

        // 2. Disputed match (should be ignored by recalculation)
        var disputedMatch = Match.Schedule(
            Guid.NewGuid(), teamA.Id, teamB.Id, _clock.UtcNow.AddDays(-2), 60,
            Guid.NewGuid(), Guid.NewGuid(), "Venue 1", Guid.NewGuid(), null, _clock.UtcNow.AddDays(-3)).Value;
        disputedMatch.SubmitResult(5, 0, teamA.Id, teamA.CaptainId, _clock.UtcNow.AddDays(-2).AddHours(1));
        disputedMatch.VerifyResult(MatchVerificationAction.Dispute, teamB.Id, teamB.CaptainId, "Score fraud", _clock.UtcNow.AddDays(-2).AddHours(2));

        // 3. Cancelled match (should be ignored by recalculation)
        var cancelledMatch = Match.Schedule(
            Guid.NewGuid(), teamA.Id, teamB.Id, _clock.UtcNow.AddDays(-1), 60,
            Guid.NewGuid(), Guid.NewGuid(), "Venue 1", Guid.NewGuid(), null, _clock.UtcNow.AddDays(-2)).Value;
        cancelledMatch.Cancel(teamA.Id, "Weather cancellation", _clock.UtcNow.AddDays(-1));

        matchesDb.Matches.AddRange(confirmedMatch, disputedMatch, cancelledMatch);
        await matchesDb.SaveChangesAsync();

        var strategy = new Glicko2RankingStrategy();
        var recalculator = new TeamRankingRecalculator(teamsDb, strategy, _clock, NullLogger<TeamRankingRecalculator>.Instance);
        var job = new RankingRecalculationJob(matchesDb, recalculator, _clock, NullLogger<RankingRecalculationJob>.Instance);

        // Act 1: First recalculation run
        var matchCount1 = await job.ExecuteRecalculationAsync();

        // Assert 1: Only 1 confirmed match replayed
        matchCount1.Should().Be(1);

        var aAfter1 = await teamsDb.Teams.FirstAsync(t => t.Id == teamA.Id);
        var bAfter1 = await teamsDb.Teams.FirstAsync(t => t.Id == teamB.Id);

        aAfter1.MatchesPlayed.Should().Be(1);
        aAfter1.Wins.Should().Be(1);
        aAfter1.Points.Should().Be(3);
        aAfter1.GoalsFor.Should().Be(2);

        bAfter1.MatchesPlayed.Should().Be(1);
        bAfter1.Losses.Should().Be(1);
        bAfter1.Points.Should().Be(0);

        var ratingA1 = aAfter1.Rating;
        var ratingB1 = bAfter1.Rating;

        // Act 2: Second recalculation run (idempotency verification)
        var matchCount2 = await job.ExecuteRecalculationAsync();

        // Assert 2: Exact identical state after second execution
        matchCount2.Should().Be(1);

        var aAfter2 = await teamsDb.Teams.FirstAsync(t => t.Id == teamA.Id);
        var bAfter2 = await teamsDb.Teams.FirstAsync(t => t.Id == teamB.Id);

        aAfter2.MatchesPlayed.Should().Be(1);
        aAfter2.Wins.Should().Be(1);
        aAfter2.Points.Should().Be(3);
        aAfter2.Rating.Should().Be(ratingA1);

        bAfter2.MatchesPlayed.Should().Be(1);
        bAfter2.Losses.Should().Be(1);
        bAfter2.Rating.Should().Be(ratingB1);
    }

    [Fact]
    public async Task GetTeamLeaderboard_WhenTeamIsSuspended_ShouldExcludeFromLeaderboardAsync()
    {
        // Arrange
        using var db = CreateTeamsDb();

        var activeTeam = Team.Create(
            Guid.NewGuid(),
            name: "Active Heroes",
            slug: "active-heroes",
            city: "Cairo",
            governorate: "Cairo",
            captainId: Guid.NewGuid(),
            inviteCode: "ACT001",
            nowUtc: _clock.UtcNow).Value;

        var suspendedTeam = Team.Create(
            Guid.NewGuid(),
            name: "Suspended Stars",
            slug: "suspended-stars",
            city: "Cairo",
            governorate: "Cairo",
            captainId: Guid.NewGuid(),
            inviteCode: "SUS001",
            nowUtc: _clock.UtcNow).Value;

        // Suspend the second team
        suspendedTeam.Suspend("Fair play violation - team brawl", _clock.UtcNow);

        db.Teams.AddRange(activeTeam, suspendedTeam);
        await db.SaveChangesAsync();

        var handler = new GetTeamLeaderboardHandler(db, new MemoryLeaderboardCache(_clock));
        var query = new GetTeamLeaderboardQuery(Page: 1, PageSize: 20);

        // Act
        var result = await handler.HandleAsync(query);

        // Assert: Test plan §P4: "suspended teams do not appear on the leaderboard"
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().NotContain(t => t.TeamId == suspendedTeam.Id);
        result.Value.TotalCount.Should().Be(1);
    }
}
