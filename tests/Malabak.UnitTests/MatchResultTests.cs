using FluentAssertions;
using Malabak.Modules.Matches.Application.Matches.GetDisputedMatches;
using Malabak.Modules.Matches.Application.Matches.SubmitResult;
using Malabak.Modules.Matches.Application.Matches.VerifyResult;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Domain.Events;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class MatchResultTests
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
    public void Match_SubmitResult_ValidScore_ShouldSucceedAndRaiseEvent()
    {
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-1), 60, null, null, null, null, null, _clock.UtcNow.AddHours(-2)).Value;

        var res = match.SubmitResult(3, 1, homeTeamId, captainA, _clock.UtcNow);

        res.IsSuccess.Should().BeTrue();
        match.HomeScore.Should().Be(3);
        match.AwayScore.Should().Be(1);
        match.ResultStatus.Should().Be(ResultVerificationStatus.PendingVerification);
        match.ResultSubmittedByTeamId.Should().Be(homeTeamId);
        match.ResultSubmittedByPlayerId.Should().Be(captainA);
        match.DomainEvents.Should().ContainSingle(e => e is MatchResultSubmittedDomainEvent);
    }

    [Fact]
    public void Match_SubmitResult_NonParticipatingTeam_ShouldFail()
    {
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-1), 60, null, null, null, null, null, _clock.UtcNow.AddHours(-2)).Value;

        var res = match.SubmitResult(2, 0, Guid.NewGuid(), Guid.NewGuid(), _clock.UtcNow);

        res.IsSuccess.Should().BeFalse();
        res.Error.Code.Should().Be("Match.NotAuthorizedCaptain");
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 100)]
    public void Match_SubmitResult_InvalidScores_ShouldFail(int homeScore, int awayScore)
    {
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-1), 60, null, null, null, null, null, _clock.UtcNow.AddHours(-2)).Value;

        var res = match.SubmitResult(homeScore, awayScore, homeTeamId, Guid.NewGuid(), _clock.UtcNow);

        res.IsSuccess.Should().BeFalse();
        res.Error.Code.Should().Be("Match.InvalidScore");
    }

    [Fact]
    public void Match_VerifyResult_SubmittingTeamCannotVerifyOwnResult()
    {
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-1), 60, null, null, null, null, null, _clock.UtcNow.AddHours(-2)).Value;

        match.SubmitResult(2, 1, homeTeamId, captainA, _clock.UtcNow);

        var verifyRes = match.VerifyResult(
            MatchVerificationAction.Confirm,
            homeTeamId, // same team that submitted!
            captainA,
            null,
            _clock.UtcNow);

        verifyRes.IsSuccess.Should().BeFalse();
        verifyRes.Error.Code.Should().Be("Match.CannotVerifyOwnResult");
    }

    [Fact]
    public void Match_VerifyResult_Confirm_ShouldCompleteMatchAndSetOutcome()
    {
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-1), 60, null, null, null, null, null, _clock.UtcNow.AddHours(-2)).Value;

        match.SubmitResult(3, 2, homeTeamId, captainA, _clock.UtcNow);

        var verifyRes = match.VerifyResult(
            MatchVerificationAction.Confirm,
            awayTeamId,
            captainB,
            null,
            _clock.UtcNow);

        verifyRes.IsSuccess.Should().BeTrue();
        match.ResultStatus.Should().Be(ResultVerificationStatus.Confirmed);
        match.Status.Should().Be(MatchStatus.Completed);
        match.Outcome.Should().Be(MatchOutcome.HomeWin);
        match.ResultVerifiedByTeamId.Should().Be(awayTeamId);
        match.DomainEvents.Should().ContainSingle(e => e is MatchResultConfirmedDomainEvent);
    }

    [Fact]
    public void Match_VerifyResult_Dispute_RequiresReason_AndLeavesRankingsUnaffected()
    {
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddHours(-1), 60, null, null, null, null, null, _clock.UtcNow.AddHours(-2)).Value;

        match.SubmitResult(1, 0, homeTeamId, captainA, _clock.UtcNow);

        // Dispute without reason should fail
        var emptyReasonRes = match.VerifyResult(
            MatchVerificationAction.Dispute,
            awayTeamId,
            captainB,
            "",
            _clock.UtcNow);

        emptyReasonRes.IsSuccess.Should().BeFalse();
        emptyReasonRes.Error.Code.Should().Be("Match.DisputeReasonRequired");

        // Dispute with valid reason
        var disputeRes = match.VerifyResult(
            MatchVerificationAction.Dispute,
            awayTeamId,
            captainB,
            "Opponent scored after full-time whistle",
            _clock.UtcNow);

        disputeRes.IsSuccess.Should().BeTrue();
        match.ResultStatus.Should().Be(ResultVerificationStatus.Disputed);
        match.Status.Should().Be(MatchStatus.Disputed);
        match.Outcome.Should().Be(MatchOutcome.None);
        match.DisputeReason.Should().Be("Opponent scored after full-time whistle");
        match.DomainEvents.Should().ContainSingle(e => e is MatchResultDisputedDomainEvent);
    }

    [Fact]
    public async Task SubmitMatchResultHandler_ShouldPersistAndNotifyOpponentCaptainAsync()
    {
        using var db = CreateInMemoryDb();
        var mockCaptaincy = new FakeCaptaincyChecker();
        var mockNotifications = new FakeNotificationPublisher();

        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        mockCaptaincy.RegisterTeam(teamA, "Red Devils", captainA);
        mockCaptaincy.RegisterTeam(teamB, "White Knights", captainB);

        var match = Match.Schedule(
            null, teamA, teamB, _clock.UtcNow.AddHours(-1), 60, null, null, null, null, null, _clock.UtcNow.AddHours(-2)).Value;
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var handler = new SubmitMatchResultHandler(db, mockCaptaincy, mockNotifications, _clock);

        var cmd = new SubmitMatchResultCommand(match.Id, teamA, 2, 1, captainA);
        var res = await handler.HandleAsync(cmd);

        res.IsSuccess.Should().BeTrue();
        res.Value.ResultStatus.Should().Be("PendingVerification");
        res.Value.HomeScore.Should().Be(2);
        res.Value.AwayScore.Should().Be(1);

        // Notification must have been sent to opponent captain (captainB)
        mockNotifications.Published.Should().ContainSingle(n =>
            n.UserId == captainB &&
            n.Type == NotificationTypes.MatchResultSubmitted);
    }

    [Fact]
    public async Task VerifyMatchResultHandler_WhenConfirmed_ShouldUpdateStatsAndNotifyCaptainsAsync()
    {
        using var db = CreateInMemoryDb();
        var mockCaptaincy = new FakeCaptaincyChecker();
        var mockRecorder = new FakeTeamMatchResultRecorder();
        var mockNotifications = new FakeNotificationPublisher();

        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        mockCaptaincy.RegisterTeam(teamA, "Red Devils", captainA);
        mockCaptaincy.RegisterTeam(teamB, "White Knights", captainB);

        var match = Match.Schedule(
            null, teamA, teamB, _clock.UtcNow.AddHours(-1), 60, null, null, null, null, null, _clock.UtcNow.AddHours(-2)).Value;
        match.SubmitResult(3, 1, teamA, captainA, _clock.UtcNow);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var handler = new VerifyMatchResultHandler(db, mockCaptaincy, mockRecorder, mockNotifications, _clock);

        var cmd = new VerifyMatchResultCommand(match.Id, teamB, MatchVerificationAction.Confirm, null, captainB);
        var res = await handler.HandleAsync(cmd);

        res.IsSuccess.Should().BeTrue();
        res.Value.ResultStatus.Should().Be("Confirmed");
        res.Value.Status.Should().Be("Completed");
        res.Value.Outcome.Should().Be("HomeWin");

        // Team stats recorder was called
        mockRecorder.RecordedCalls.Should().ContainSingle(c =>
            c.HomeTeamId == teamA &&
            c.AwayTeamId == teamB &&
            c.HomeScore == 3 &&
            c.AwayScore == 1);

        // Notifications sent to both captains
        mockNotifications.Published.Should().Contain(n => n.UserId == captainA && n.Type == NotificationTypes.MatchResultConfirmed);
        mockNotifications.Published.Should().Contain(n => n.UserId == captainB && n.Type == NotificationTypes.MatchResultConfirmed);
    }

    [Fact]
    public async Task VerifyMatchResultHandler_WhenDisputed_ShouldNotUpdateTeamStats_AndRouteToAdminAsync()
    {
        using var db = CreateInMemoryDb();
        var mockCaptaincy = new FakeCaptaincyChecker();
        var mockRecorder = new FakeTeamMatchResultRecorder();
        var mockNotifications = new FakeNotificationPublisher();

        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        mockCaptaincy.RegisterTeam(teamA, "Red Devils", captainA);
        mockCaptaincy.RegisterTeam(teamB, "White Knights", captainB);

        var match = Match.Schedule(
            null, teamA, teamB, _clock.UtcNow.AddHours(-1), 60, null, null, null, null, null, _clock.UtcNow.AddHours(-2)).Value;
        match.SubmitResult(2, 0, teamA, captainA, _clock.UtcNow);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var handler = new VerifyMatchResultHandler(db, mockCaptaincy, mockRecorder, mockNotifications, _clock);

        var cmd = new VerifyMatchResultCommand(
            match.Id,
            teamB,
            MatchVerificationAction.Dispute,
            "False score reported",
            captainB);

        var res = await handler.HandleAsync(cmd);

        res.IsSuccess.Should().BeTrue();
        res.Value.ResultStatus.Should().Be("Disputed");
        res.Value.Status.Should().Be("Disputed");
        res.Value.Outcome.Should().Be("None");

        // Team stats MUST NOT be updated
        mockRecorder.RecordedCalls.Should().BeEmpty();

        // Notification sent to other captain
        mockNotifications.Published.Should().ContainSingle(n => n.UserId == captainA && n.Type == NotificationTypes.MatchResultDisputed);

        // Admin disputes queue contains this match
        var queueHandler = new GetDisputedMatchesHandler(db, mockCaptaincy);
        var queueRes = await queueHandler.HandleAsync(new GetDisputedMatchesQuery());

        queueRes.IsSuccess.Should().BeTrue();
        queueRes.Value.TotalCount.Should().Be(1);
        queueRes.Value.Items.Should().ContainSingle(d =>
            d.MatchId == match.Id &&
            d.DisputingTeamName == "White Knights" &&
            d.DisputeReason == "False score reported");
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
