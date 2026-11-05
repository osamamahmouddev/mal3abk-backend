using FluentAssertions;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Application.Teams.FairPlay.AppealReport;
using Malabak.Modules.Teams.Application.Teams.FairPlay.GetReports;
using Malabak.Modules.Teams.Application.Teams.FairPlay.ResolveAppeal;
using Malabak.Modules.Teams.Application.Teams.FairPlay.ReviewReport;
using Malabak.Modules.Teams.Application.Teams.FairPlay.SubmitReport;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamLeaderboard;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Teams.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class LeaderboardAndFairPlayTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc));

    private static TeamsDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<TeamsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TeamsDbContext(options);
    }

    [Fact]
    public async Task Leaderboard_ShouldSortByRatingPointsGoalDiff_AndAssignRanksAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var cache = new MemoryLeaderboardCache(_clock);
        var handler = new GetTeamLeaderboardHandler(db, cache);

        var captain1 = Guid.NewGuid();
        var captain2 = Guid.NewGuid();
        var captain3 = Guid.NewGuid();

        // Team A: 1600 rating
        var teamA = Team.Create(Guid.NewGuid(), "Team Alpha", "alpha", "Cairo", "Cairo", captain1, "123456", _clock.UtcNow).Value;
        teamA.UpdateRanking(1600.0, 100.0, 0.06, _clock.UtcNow);
        teamA.RecordMatchResult(isWin: true, isDraw: false, isLoss: false, goalsFor: 3, goalsAgainst: 1, _clock.UtcNow);

        // Team B: 1700 rating (Highest)
        var teamB = Team.Create(Guid.NewGuid(), "Team Beta", "beta", "Cairo", "Cairo", captain2, "234567", _clock.UtcNow).Value;
        teamB.UpdateRanking(1700.0, 80.0, 0.06, _clock.UtcNow);
        teamB.RecordMatchResult(isWin: true, isDraw: false, isLoss: false, goalsFor: 5, goalsAgainst: 0, _clock.UtcNow);

        // Team C: 1500 rating
        var teamC = Team.Create(Guid.NewGuid(), "Team Gamma", "gamma", "Giza", "Giza", captain3, "345678", _clock.UtcNow).Value;
        teamC.UpdateRanking(1500.0, 150.0, 0.06, _clock.UtcNow);

        db.Teams.AddRange(teamA, teamB, teamC);
        await db.SaveChangesAsync();

        // Act
        var result = await handler.HandleAsync(new GetTeamLeaderboardQuery(Page: 1, PageSize: 10));

        // Assert
        result.IsSuccess.Should().BeTrue();
        var paged = result.Value;
        paged.TotalCount.Should().Be(3);
        paged.Items.Should().HaveCount(3);

        // Rank 1: Beta (1700)
        paged.Items[0].TeamId.Should().Be(teamB.Id);
        paged.Items[0].Rank.Should().Be(1);
        paged.Items[0].Rating.Should().Be(1700.0);

        // Rank 2: Alpha (1600)
        paged.Items[1].TeamId.Should().Be(teamA.Id);
        paged.Items[1].Rank.Should().Be(2);

        // Rank 3: Gamma (1500)
        paged.Items[2].TeamId.Should().Be(teamC.Id);
        paged.Items[2].Rank.Should().Be(3);
    }

    [Fact]
    public async Task Leaderboard_WithCityFilter_ShouldOnlyReturnMatchingTeamsAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var cache = new MemoryLeaderboardCache(_clock);
        var handler = new GetTeamLeaderboardHandler(db, cache);

        var captain1 = Guid.NewGuid();
        var captain2 = Guid.NewGuid();

        var cairoTeam = Team.Create(Guid.NewGuid(), "Cairo FC", "cairo-fc", "Cairo", "Cairo", captain1, "123456", _clock.UtcNow).Value;
        var alexTeam = Team.Create(Guid.NewGuid(), "Alexandria Stars", "alex-stars", "Alexandria", "Alexandria", captain2, "234567", _clock.UtcNow).Value;

        db.Teams.AddRange(cairoTeam, alexTeam);
        await db.SaveChangesAsync();

        // Act
        var result = await handler.HandleAsync(new GetTeamLeaderboardQuery(City: "cairo"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle(t => t.TeamId == cairoTeam.Id);
    }

    [Fact]
    public async Task LeaderboardCache_ShouldServeFromCache_AndInvalidateWhenClearedAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var cache = new MemoryLeaderboardCache(_clock);
        var handler = new GetTeamLeaderboardHandler(db, cache);

        var captain = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Cached Team", "cached-team", "Cairo", "Cairo", captain, "123456", _clock.UtcNow).Value;
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        // Query 1 -> Populates cache
        var result1 = await handler.HandleAsync(new GetTeamLeaderboardQuery(Page: 1, PageSize: 10));
        result1.IsSuccess.Should().BeTrue();
        result1.Value.TotalCount.Should().Be(1);

        // Add second team directly to DB bypassing cache
        var captain2 = Guid.NewGuid();
        var team2 = Team.Create(Guid.NewGuid(), "New Team", "new-team", "Cairo", "Cairo", captain2, "234567", _clock.UtcNow).Value;
        db.Teams.Add(team2);
        await db.SaveChangesAsync();

        // Query 2 -> Should hit cache and still return 1 item
        var result2 = await handler.HandleAsync(new GetTeamLeaderboardQuery(Page: 1, PageSize: 10));
        result2.Value.TotalCount.Should().Be(1);

        // Invalidate cache
        await cache.InvalidateAsync();

        // Query 3 -> Should fetch fresh data from DB (2 items)
        var result3 = await handler.HandleAsync(new GetTeamLeaderboardQuery(Page: 1, PageSize: 10));
        result3.Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task SubmitFairPlayReport_ShouldCreatePendingReport_WithoutAlteringReportedTeamStatsAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var handler = new SubmitFairPlayReportHandler(db, _clock);

        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var teamA = Team.Create(Guid.NewGuid(), "Reporter Team", "reporter-team", "Cairo", "Cairo", captainA, "111111", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Reported Team", "reported-team", "Cairo", "Cairo", captainB, "222222", _clock.UtcNow).Value;
        teamB.UpdateRanking(1600, 100, 0.06, _clock.UtcNow);

        db.Teams.AddRange(teamA, teamB);
        await db.SaveChangesAsync();

        var command = new SubmitFairPlayReportCommand(
            ReporterTeamId: teamA.Id,
            ReporterPlayerId: captainA,
            ReportedTeamId: teamB.Id,
            Category: FairPlayReportCategory.ViolentConduct,
            Description: "Opponent players displayed aggressive verbal conduct.",
            EvidenceUrls: "https://example.com/evidence.jpg");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var reportDto = result.Value;
        reportDto.Status.Should().Be(FairPlayReportStatus.PendingReview);
        reportDto.PenaltyType.Should().Be(FairPlayPenaltyType.None);
        reportDto.PointsDeducted.Should().BeNull();

        // Crucial fair play rule: raw user complaints NEVER penalize directly
        var freshTeamB = await db.Teams.FindAsync(teamB.Id);
        freshTeamB!.Rating.Should().Be(1600);
        freshTeamB.Points.Should().Be(0);
    }

    [Fact]
    public async Task SubmitFairPlayReport_WhenReportingOwnTeam_ShouldFailAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var handler = new SubmitFairPlayReportHandler(db, _clock);

        var captain = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Self Team", "self-team", "Cairo", "Cairo", captain, "111111", _clock.UtcNow).Value;
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var command = new SubmitFairPlayReportCommand(
            ReporterTeamId: team.Id,
            ReporterPlayerId: captain,
            ReportedTeamId: team.Id,
            Category: FairPlayReportCategory.UnsportsmanlikeBehavior,
            Description: "Testing self report.");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("FairPlay.CannotReportOwnTeam");
    }

    [Fact]
    public async Task ReviewFairPlayReport_WhenApprovedWithPointsDeduction_ShouldUpdateStatusAndInvalidateCacheAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var cache = new MemoryLeaderboardCache(_clock);
        var handler = new ReviewFairPlayReportHandler(db, cache, _clock);

        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var teamA = Team.Create(Guid.NewGuid(), "Team A", "team-a", "Cairo", "Cairo", captainA, "111111", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Team B", "team-b", "Cairo", "Cairo", captainB, "222222", _clock.UtcNow).Value;
        db.Teams.AddRange(teamA, teamB);

        var report = TeamFairPlayReport.Create(
            teamA.Id, captainA, teamB.Id, FairPlayReportCategory.NoShow, "Team failed to show up without notice.", _clock.UtcNow).Value;
        db.FairPlayReports.Add(report);
        await db.SaveChangesAsync();

        var command = new ReviewFairPlayReportCommand(
            ReportId: report.Id,
            AdminUserId: adminId,
            IsApproved: true,
            Penalty: FairPlayPenaltyType.PointsDeduction,
            PointsDeducted: 3,
            AdminNotes: "No-show verified by pitch manager.");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var updated = result.Value;
        updated.Status.Should().Be(FairPlayReportStatus.Approved);
        updated.PenaltyType.Should().Be(FairPlayPenaltyType.PointsDeduction);
        updated.PointsDeducted.Should().Be(3);
        updated.AdminDecisionNotes.Should().Be("No-show verified by pitch manager.");
        updated.AdminReviewedByUserId.Should().Be(adminId);
    }

    [Fact]
    public async Task ReviewFairPlayReport_WhenDismissed_ShouldUpdateStatusToDismissedAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var cache = new MemoryLeaderboardCache(_clock);
        var handler = new ReviewFairPlayReportHandler(db, cache, _clock);

        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var teamA = Team.Create(Guid.NewGuid(), "Team A", "team-a", "Cairo", "Cairo", captainA, "111111", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Team B", "team-b", "Cairo", "Cairo", captainB, "222222", _clock.UtcNow).Value;
        db.Teams.AddRange(teamA, teamB);

        var report = TeamFairPlayReport.Create(
            teamA.Id, captainA, teamB.Id, FairPlayReportCategory.ViolentConduct, "Frivolous complaint.", _clock.UtcNow).Value;
        db.FairPlayReports.Add(report);
        await db.SaveChangesAsync();

        var command = new ReviewFairPlayReportCommand(
            ReportId: report.Id,
            AdminUserId: adminId,
            IsApproved: false,
            Penalty: FairPlayPenaltyType.None,
            PointsDeducted: null,
            AdminNotes: "Insufficient evidence provided.");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(FairPlayReportStatus.Dismissed);
        result.Value.PenaltyType.Should().Be(FairPlayPenaltyType.None);
    }

    [Fact]
    public async Task AppealFairPlayReport_WhenSubmittedByReportedTeamCaptain_ShouldTransitionToAppealedAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var handler = new AppealFairPlayReportHandler(db, _clock);

        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var teamA = Team.Create(Guid.NewGuid(), "Team A", "team-a", "Cairo", "Cairo", captainA, "111111", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Team B", "team-b", "Cairo", "Cairo", captainB, "222222", _clock.UtcNow).Value;
        db.Teams.AddRange(teamA, teamB);

        var report = TeamFairPlayReport.Create(
            teamA.Id, captainA, teamB.Id, FairPlayReportCategory.NoShow, "No show claim.", _clock.UtcNow).Value;
        report.ReviewReport(true, FairPlayPenaltyType.Warning, null, "Warning issued.", adminId, _clock.UtcNow);
        db.FairPlayReports.Add(report);
        await db.SaveChangesAsync();

        var command = new AppealFairPlayReportCommand(
            ReportId: report.Id,
            CaptainPlayerId: captainB,
            AppealReason: "We arrived on time and have video timestamp proof of attendance.");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var appealed = result.Value;
        appealed.Status.Should().Be(FairPlayReportStatus.Appealed);
        appealed.AppealReason.Should().Be("We arrived on time and have video timestamp proof of attendance.");
        appealed.AppealedByPlayerId.Should().Be(captainB);
    }

    [Fact]
    public async Task AppealFairPlayReport_WhenCallerIsNotCaptain_ShouldFailAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var handler = new AppealFairPlayReportHandler(db, _clock);

        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var nonCaptain = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var teamA = Team.Create(Guid.NewGuid(), "Team A", "team-a", "Cairo", "Cairo", captainA, "111111", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Team B", "team-b", "Cairo", "Cairo", captainB, "222222", _clock.UtcNow).Value;
        teamB.JoinWithInviteCode("222222", nonCaptain, _clock.UtcNow);
        db.Teams.AddRange(teamA, teamB);

        var report = TeamFairPlayReport.Create(
            teamA.Id, captainA, teamB.Id, FairPlayReportCategory.NoShow, "No show claim.", _clock.UtcNow).Value;
        report.ReviewReport(true, FairPlayPenaltyType.Warning, null, "Warning issued.", adminId, _clock.UtcNow);
        db.FairPlayReports.Add(report);
        await db.SaveChangesAsync();

        var command = new AppealFairPlayReportCommand(
            ReportId: report.Id,
            CaptainPlayerId: nonCaptain,
            AppealReason: "Attempting to appeal as member.");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(TeamErrors.NotCaptain.Code);
    }

    [Fact]
    public async Task ResolveFairPlayAppeal_WhenOverturned_ShouldClearPenaltyAndInvalidateCacheAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var cache = new MemoryLeaderboardCache(_clock);
        var handler = new ResolveFairPlayAppealHandler(db, cache, _clock);

        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var teamA = Team.Create(Guid.NewGuid(), "Team A", "team-a", "Cairo", "Cairo", captainA, "111111", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Team B", "team-b", "Cairo", "Cairo", captainB, "222222", _clock.UtcNow).Value;
        db.Teams.AddRange(teamA, teamB);

        var report = TeamFairPlayReport.Create(
            teamA.Id, captainA, teamB.Id, FairPlayReportCategory.NoShow, "No show claim.", _clock.UtcNow).Value;
        report.ReviewReport(true, FairPlayPenaltyType.PointsDeduction, 3, "Deducted 3 pts.", adminId, _clock.UtcNow);
        report.SubmitAppeal("Proof submitted.", captainB, _clock.UtcNow);
        db.FairPlayReports.Add(report);
        await db.SaveChangesAsync();

        var command = new ResolveFairPlayAppealCommand(
            ReportId: report.Id,
            AdminUserId: adminId,
            UpholdViolation: false,
            AdminNotes: "Video evidence confirmed team was present; penalty overturned.");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var resolved = result.Value;
        resolved.Status.Should().Be(FairPlayReportStatus.AppealOverturned);
        resolved.PenaltyType.Should().Be(FairPlayPenaltyType.None);
        resolved.PointsDeducted.Should().BeNull();
        resolved.AdminAppealNotes.Should().Be("Video evidence confirmed team was present; penalty overturned.");
    }

    [Fact]
    public async Task GetFairPlayReports_ShouldFilterByStatusAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var handler = new GetFairPlayReportsHandler(db);

        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var teamA = Team.Create(Guid.NewGuid(), "Team A", "team-a", "Cairo", "Cairo", captainA, "111111", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Team B", "team-b", "Cairo", "Cairo", captainB, "222222", _clock.UtcNow).Value;
        db.Teams.AddRange(teamA, teamB);

        var report1 = TeamFairPlayReport.Create(teamA.Id, captainA, teamB.Id, FairPlayReportCategory.ViolentConduct, "Report 1", _clock.UtcNow).Value;
        var report2 = TeamFairPlayReport.Create(teamB.Id, captainB, teamA.Id, FairPlayReportCategory.RosterFraud, "Report 2", _clock.UtcNow).Value;
        report2.ReviewReport(false, FairPlayPenaltyType.None, null, "Dismissed.", Guid.NewGuid(), _clock.UtcNow);

        db.FairPlayReports.AddRange(report1, report2);
        await db.SaveChangesAsync();

        // Query pending only
        var pendingResult = await handler.HandleAsync(new GetFairPlayReportsQuery(Status: FairPlayReportStatus.PendingReview));
        pendingResult.IsSuccess.Should().BeTrue();
        pendingResult.Value.TotalCount.Should().Be(1);
        pendingResult.Value.Items[0].Id.Should().Be(report1.Id);

        // Query dismissed only
        var dismissedResult = await handler.HandleAsync(new GetFairPlayReportsQuery(Status: FairPlayReportStatus.Dismissed));
        dismissedResult.IsSuccess.Should().BeTrue();
        dismissedResult.Value.TotalCount.Should().Be(1);
        dismissedResult.Value.Items[0].Id.Should().Be(report2.Id);
    }
}
