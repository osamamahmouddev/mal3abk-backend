using FluentAssertions;
using Malabak.Modules.Matches.Application.Matches.GetMatchAuditHistory;
using Malabak.Modules.Matches.Application.Matches.ResolveDispute;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Domain.Events;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.Modules.Matches.Infrastructure.Jobs;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Events;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class MatchDisputeModerationTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc));

    private static MatchesDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<MatchesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MatchesDbContext(options);
    }

    [Fact]
    public async Task TimeoutJob_WhenPendingPast24Hours_ShouldAutoConfirmAndDispatchEventsAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var fakeDispatcher = new FakeDomainEventDispatcher();
        var job = new MatchResultTimeoutJob(db, fakeDispatcher, _clock, NullLogger<MatchResultTimeoutJob>.Instance);

        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var captainA = Guid.NewGuid();

        // Match 1: Submitted 25 hours ago (right after it ended) -> eligible for timeout auto-confirm
        var match1 = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-26), 60, null, null, null, null, null, _clock.UtcNow.AddDays(-3)).Value;
        match1.SubmitResult(2, 1, homeTeamId, captainA, _clock.UtcNow.AddHours(-25));

        // Match 2: Submitted 5 hours ago -> NOT eligible
        var match2 = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-6), 60, null, null, null, null, null, _clock.UtcNow.AddDays(-1)).Value;
        match2.SubmitResult(1, 1, homeTeamId, captainA, _clock.UtcNow.AddHours(-5));

        db.Matches.AddRange(match1, match2);
        await db.SaveChangesAsync();

        // Act
        var autoConfirmedCount = await job.ExecuteSweepAsync();

        // Assert
        autoConfirmedCount.Should().Be(1);

        var updatedMatch1 = await db.Matches.FirstAsync(m => m.Id == match1.Id);
        updatedMatch1.ResultStatus.Should().Be(ResultVerificationStatus.AutoConfirmed);
        updatedMatch1.Status.Should().Be(MatchStatus.Completed);
        updatedMatch1.Outcome.Should().Be(MatchOutcome.HomeWin);

        var auditRecords1 = await db.MatchResultAuditRecords.Where(a => a.MatchId == match1.Id).ToListAsync();
        auditRecords1.Should().Contain(a => a.ChangeType == "AutoConfirmation");

        var updatedMatch2 = await db.Matches.FirstAsync(m => m.Id == match2.Id);
        updatedMatch2.ResultStatus.Should().Be(ResultVerificationStatus.PendingVerification);

        fakeDispatcher.DispatchedEvents.Should().ContainSingle(e => e is MatchResultConfirmedDomainEvent);
    }

    [Fact]
    public async Task ResolveDispute_WhenSetOfficialScore_ShouldUpdateScoreAndCompleteMatchAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-2), 60, null, null, null, null, null, _clock.UtcNow.AddDays(-1)).Value;
        match.SubmitResult(3, 0, homeTeamId, captainA, _clock.UtcNow.AddHours(-1));
        match.VerifyResult(MatchVerificationAction.Dispute, awayTeamId, captainB, "Score was actually 1 - 2, referee sheet attached.", _clock.UtcNow.AddMinutes(-30));

        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var fakeCaptaincy = new FakeCaptaincyChecker();
        fakeCaptaincy.RegisterTeam(homeTeamId, "Home FC", captainA);
        fakeCaptaincy.RegisterTeam(awayTeamId, "Away United", captainB);

        var fakeRecalculator = new FakeRankingRecalculator();
        var recalculationJob = new RankingRecalculationJob(db, fakeRecalculator, _clock, NullLogger<RankingRecalculationJob>.Instance);
        var fakeNotifications = new FakeNotificationPublisher();

        var handler = new ResolveMatchDisputeHandler(
            db,
            fakeCaptaincy,
            recalculationJob,
            fakeNotifications,
            _clock,
            NullLogger<ResolveMatchDisputeHandler>.Instance);

        var command = new ResolveMatchDisputeCommand(
            match.Id,
            adminId,
            MatchDisputeResolutionAction.SetOfficialScore,
            "Reviewed referee official score sheet. Away team won 2 - 1.",
            OfficialHomeScore: 1,
            OfficialAwayScore: 2);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.HomeScore.Should().Be(1);
        result.Value.AwayScore.Should().Be(2);
        result.Value.Status.Should().Be(MatchStatus.Completed.ToString());

        var updatedMatch = await db.Matches.FirstAsync(m => m.Id == match.Id);
        updatedMatch.Status.Should().Be(MatchStatus.Completed);
        updatedMatch.ResultStatus.Should().Be(ResultVerificationStatus.Confirmed);
        updatedMatch.Outcome.Should().Be(MatchOutcome.AwayWin);
        updatedMatch.AdminResolvedByUserId.Should().Be(adminId);

        var auditRecords = await db.MatchResultAuditRecords.Where(a => a.MatchId == match.Id).ToListAsync();
        auditRecords.Should().Contain(a => a.ChangeType == "AdminResolution");

        // Verify notification delivered to both captains
        fakeNotifications.Published.Should().Contain(n => n.UserId == captainA && n.Type == NotificationTypes.MatchDisputeResolved);
        fakeNotifications.Published.Should().Contain(n => n.UserId == captainB && n.Type == NotificationTypes.MatchDisputeResolved);
    }

    [Fact]
    public async Task ResolveDispute_WhenCancelMatch_ShouldTransitionToCancelledAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-2), 60, null, null, null, null, null, _clock.UtcNow.AddDays(-1)).Value;
        match.SubmitResult(2, 2, homeTeamId, captainA, _clock.UtcNow.AddHours(-1));
        match.VerifyResult(MatchVerificationAction.Dispute, awayTeamId, captainB, "Match was abandoned due to pitch flood.", _clock.UtcNow.AddMinutes(-30));

        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var fakeCaptaincy = new FakeCaptaincyChecker();
        fakeCaptaincy.RegisterTeam(homeTeamId, "Home FC", captainA);
        fakeCaptaincy.RegisterTeam(awayTeamId, "Away United", captainB);

        var fakeRecalculator = new FakeRankingRecalculator();
        var recalculationJob = new RankingRecalculationJob(db, fakeRecalculator, _clock, NullLogger<RankingRecalculationJob>.Instance);
        var fakeNotifications = new FakeNotificationPublisher();

        var handler = new ResolveMatchDisputeHandler(
            db,
            fakeCaptaincy,
            recalculationJob,
            fakeNotifications,
            _clock,
            NullLogger<ResolveMatchDisputeHandler>.Instance);

        var command = new ResolveMatchDisputeCommand(
            match.Id,
            adminId,
            MatchDisputeResolutionAction.CancelMatch,
            "Confirmed with venue owner that match was abandoned due to pitch flood.");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(MatchStatus.Cancelled.ToString());

        var updatedMatch = await db.Matches.FirstAsync(m => m.Id == match.Id);
        updatedMatch.Status.Should().Be(MatchStatus.Cancelled);
        updatedMatch.Outcome.Should().Be(MatchOutcome.None);
    }

    [Fact]
    public async Task GetMatchAuditHistory_ShouldReturnChronologicalHistoryAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-2), 60, null, null, null, null, null, _clock.UtcNow.AddDays(-1)).Value;
        match.SubmitResult(3, 1, homeTeamId, captainA, _clock.UtcNow.AddHours(-1));
        match.VerifyResult(MatchVerificationAction.Dispute, awayTeamId, captainB, "Offside dispute", _clock.UtcNow.AddMinutes(-30));

        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var handler = new GetMatchAuditHistoryHandler(db);
        var query = new GetMatchAuditHistoryQuery(match.Id);

        // Act
        var result = await handler.HandleAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(a => a.ChangeType == "SubmitResult");
        result.Value.Should().Contain(a => a.ChangeType == "Dispute");
    }

    private sealed class FakeDomainEventDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> DispatchedEvents { get; } = [];

        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            DispatchedEvents.Add(domainEvent);
            return Task.CompletedTask;
        }

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        {
            DispatchedEvents.AddRange(domainEvents);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCaptaincyChecker : ITeamCaptaincyChecker
    {
        private readonly Dictionary<Guid, (string Name, Guid CaptainId)> _teams = [];

        public void RegisterTeam(Guid teamId, string name, Guid captainId)
        {
            _teams[teamId] = (name, captainId);
        }

        public Task<bool> IsCaptainOrViceCaptainAsync(Guid teamId, Guid playerId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_teams.TryGetValue(teamId, out var info) && info.CaptainId == playerId);
        }

        public Task<Guid?> GetCaptainPlayerIdAsync(Guid teamId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Guid?>(_teams.TryGetValue(teamId, out var info) ? info.CaptainId : null);
        }

        public Task<string?> GetTeamNameAsync(Guid teamId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(_teams.TryGetValue(teamId, out var info) ? info.Name : null);
        }

        public Task<bool> CanBeChallengedAsync(Guid teamId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_teams.ContainsKey(teamId));
        }
    }

    private sealed class FakeRankingRecalculator : ITeamRankingRecalculator
    {
        public Task<Result> RecalculateAllAsync(IReadOnlyList<ConfirmedMatchRecord> confirmedMatches, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakeNotificationPublisher : INotificationPublisher
    {
        public List<NotificationRequest> Published { get; } = [];

        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            Published.Add(request);
            return Task.CompletedTask;
        }
    }
}
