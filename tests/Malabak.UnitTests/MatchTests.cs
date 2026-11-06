using FluentAssertions;
using Malabak.Modules.Matches.Application.Challenges.LinkBooking;
using Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Application.Matches.CancelMatch;
using Malabak.Modules.Matches.Application.Matches.GetMatchById;
using Malabak.Modules.Matches.Application.Matches.GetMatchHistory;
using Malabak.Modules.Matches.Application.Matches.GetUpcomingMatches;
using Malabak.Modules.Matches.Application.Matches.LinkBooking;
using Malabak.Modules.Matches.Application.Matches.ScheduleMatch;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Domain.Events;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class MatchTests
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
    public void Match_Schedule_ValidParameters_ShouldSucceedAndRaiseEvent()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var scheduledStart = _clock.UtcNow.AddDays(2);
        var duration = 90;

        // Act
        var result = Match.Schedule(
            challengeId: null,
            homeTeamId: homeTeamId,
            awayTeamId: awayTeamId,
            scheduledStartUtc: scheduledStart,
            durationMinutes: duration,
            pitchId: Guid.NewGuid(),
            venueId: Guid.NewGuid(),
            venueName: "Zamalek SC",
            bookingId: null,
            notes: "League Qualifier",
            nowUtc: _clock.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var match = result.Value;
        match.HomeTeamId.Should().Be(homeTeamId);
        match.AwayTeamId.Should().Be(awayTeamId);
        match.ScheduledStartUtc.Should().Be(scheduledStart);
        match.DurationMinutes.Should().Be(duration);
        match.ScheduledEndUtc.Should().Be(scheduledStart.AddMinutes(duration));
        match.Status.Should().Be(MatchStatus.Scheduled);
        match.Outcome.Should().Be(MatchOutcome.None);
        match.DomainEvents.Should().ContainSingle(e => e is MatchScheduledDomainEvent);
    }

    [Fact]
    public void Match_Schedule_SameTeam_ShouldFail()
    {
        var teamId = Guid.NewGuid();
        var result = Match.Schedule(
            challengeId: null,
            homeTeamId: teamId,
            awayTeamId: teamId,
            scheduledStartUtc: _clock.UtcNow.AddDays(1),
            durationMinutes: 60,
            pitchId: null,
            venueId: null,
            venueName: null,
            bookingId: null,
            notes: null,
            nowUtc: _clock.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Match.SameTeam");
    }

    [Theory]
    [InlineData(20)]
    [InlineData(300)]
    public void Match_Schedule_InvalidDuration_ShouldFail(int duration)
    {
        var result = Match.Schedule(
            challengeId: null,
            homeTeamId: Guid.NewGuid(),
            awayTeamId: Guid.NewGuid(),
            scheduledStartUtc: _clock.UtcNow.AddDays(1),
            durationMinutes: duration,
            pitchId: null,
            venueId: null,
            venueName: null,
            bookingId: null,
            notes: null,
            nowUtc: _clock.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Match.InvalidDuration");
    }

    [Fact]
    public void Match_Schedule_PastDate_ShouldFail()
    {
        var result = Match.Schedule(
            challengeId: null,
            homeTeamId: Guid.NewGuid(),
            awayTeamId: Guid.NewGuid(),
            scheduledStartUtc: _clock.UtcNow.AddHours(-2),
            durationMinutes: 60,
            pitchId: null,
            venueId: null,
            venueName: null,
            bookingId: null,
            notes: null,
            nowUtc: _clock.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Match.InvalidScheduledTime");
    }

    [Fact]
    public void Match_LinkBooking_ByParticipatingTeam_ShouldSucceed()
    {
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddDays(2), 60, null, null, null, null, null, _clock.UtcNow).Value;

        var bookingId = Guid.NewGuid();
        var linkResult = match.LinkBooking(bookingId, awayTeamId, _clock.UtcNow);

        linkResult.IsSuccess.Should().BeTrue();
        match.BookingId.Should().Be(bookingId);
    }

    [Fact]
    public void Match_LinkBooking_ByNonParticipatingTeam_ShouldFail()
    {
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var strangerTeamId = Guid.NewGuid();
        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddDays(2), 60, null, null, null, null, null, _clock.UtcNow).Value;

        var linkResult = match.LinkBooking(Guid.NewGuid(), strangerTeamId, _clock.UtcNow);

        linkResult.IsSuccess.Should().BeFalse();
        linkResult.Error.Code.Should().Be("Match.NotAuthorizedCaptain");
    }

    [Fact]
    public void Match_Cancel_ByParticipatingTeam_ShouldSucceedAndTransitionStatus()
    {
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var match = Match.Schedule(
            null, homeTeamId, awayTeamId, _clock.UtcNow.AddDays(2), 60, null, null, null, null, null, _clock.UtcNow).Value;

        var cancelResult = match.Cancel(homeTeamId, "Injured squad", _clock.UtcNow);

        cancelResult.IsSuccess.Should().BeTrue();
        match.Status.Should().Be(MatchStatus.Cancelled);
        match.Outcome.Should().Be(MatchOutcome.Cancelled);
        match.CancelledByTeamId.Should().Be(homeTeamId);
        match.CancellationReason.Should().Be("Injured squad");
        match.DomainEvents.Should().ContainSingle(e => e is MatchCancelledDomainEvent);
    }

    [Fact]
    public void Match_GetBookingDeepLink_ShouldContainCorrectParameters()
    {
        var pitchId = Guid.NewGuid();
        var venueId = Guid.NewGuid();
        var match = Match.Schedule(
            null, Guid.NewGuid(), Guid.NewGuid(), _clock.UtcNow.AddDays(3), 90, pitchId, venueId, "Arena", null, null, _clock.UtcNow).Value;

        var deepLink = match.GetBookingDeepLink();

        deepLink.Should().StartWith("malabak://booking/create");
        deepLink.Should().Contain($"matchId={match.Id:D}");
        deepLink.Should().Contain($"pitchId={pitchId:D}");
        deepLink.Should().Contain($"venueId={venueId:D}");
        deepLink.Should().Contain("duration=90");
    }

    [Fact]
    public async Task ScheduleMatchHandler_WhenValidAndNoConflict_ShouldPersistAndNotifyAsync()
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

        var handler = new ScheduleMatchHandler(db, mockCaptaincy, mockNotifications, _clock);

        var cmd = new ScheduleMatchCommand(
            teamA,
            teamB,
            _clock.UtcNow.AddDays(2),
            60,
            null,
            null,
            "Smouha Pitch",
            null,
            "Friendly game",
            captainA);

        var res = await handler.HandleAsync(cmd);

        res.IsSuccess.Should().BeTrue();
        res.Value.HomeTeamName.Should().Be("Red Devils");
        res.Value.AwayTeamName.Should().Be("White Knights");
        res.Value.Status.Should().Be("Scheduled");

        // Notification sent to away captain
        mockNotifications.Published.Should().ContainSingle(n => n.UserId == captainB && n.Type == NotificationTypes.MatchScheduled);

        // Match persisted in DB
        var savedMatch = await db.Matches.FirstOrDefaultAsync(m => m.Id == res.Value.Id);
        savedMatch.Should().NotBeNull();
    }

    [Fact]
    public async Task ScheduleMatchHandler_WhenConflictExists_ShouldReturnConflictErrorAsync()
    {
        using var db = CreateInMemoryDb();
        var mockCaptaincy = new FakeCaptaincyChecker();
        var mockNotifications = new FakeNotificationPublisher();

        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var teamC = Guid.NewGuid();
        var captainA = Guid.NewGuid();

        mockCaptaincy.RegisterTeam(teamA, "Red Devils", captainA);
        mockCaptaincy.RegisterTeam(teamB, "White Knights", Guid.NewGuid());
        mockCaptaincy.RegisterTeam(teamC, "Green Falcons", Guid.NewGuid());

        var matchStartTime = _clock.UtcNow.AddDays(1);
        var existingMatch = Match.Schedule(
            null, teamA, teamB, matchStartTime, 60, null, null, null, null, null, _clock.UtcNow).Value;
        db.Matches.Add(existingMatch);
        await db.SaveChangesAsync();

        var handler = new ScheduleMatchHandler(db, mockCaptaincy, mockNotifications, _clock);

        // Team A tries to schedule another match overlapping the same time window (with Team C)
        var cmd = new ScheduleMatchCommand(
            teamA,
            teamC,
            matchStartTime.AddMinutes(30), // 30 minutes in -> overlaps!
            60,
            null,
            null,
            null,
            null,
            null,
            captainA);

        var res = await handler.HandleAsync(cmd);

        res.IsSuccess.Should().BeFalse();
        res.Error.Code.Should().Be("Match.ScheduleConflict");
    }

    [Fact]
    public async Task UpcomingAndHistoryQueries_ShouldReturnCorrectPagedResultsAsync()
    {
        using var db = CreateInMemoryDb();
        var mockCaptaincy = new FakeCaptaincyChecker();
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        mockCaptaincy.RegisterTeam(teamA, "Team Alpha", Guid.NewGuid());
        mockCaptaincy.RegisterTeam(teamB, "Team Beta", Guid.NewGuid());

        // 1. Upcoming match
        var upcomingMatch = Match.Schedule(
            null, teamA, teamB, _clock.UtcNow.AddDays(2), 60, null, null, null, null, null, _clock.UtcNow).Value;
        db.Matches.Add(upcomingMatch);

        // 2. Past cancelled match
        var cancelledMatch = Match.Schedule(
            null, teamA, teamB, _clock.UtcNow.AddDays(3), 60, null, null, null, null, null, _clock.UtcNow).Value;
        cancelledMatch.Cancel(teamA, "Raining heavily", _clock.UtcNow);
        db.Matches.Add(cancelledMatch);

        await db.SaveChangesAsync();

        var upcomingHandler = new GetUpcomingMatchesHandler(db, mockCaptaincy, _clock);
        var historyHandler = new GetMatchHistoryHandler(db, mockCaptaincy, _clock);
        var getByIdHandler = new GetMatchByIdHandler(db, mockCaptaincy);

        // Act & Assert Upcoming
        var upcomingRes = await upcomingHandler.HandleAsync(new GetUpcomingMatchesQuery(TeamId: teamA));
        upcomingRes.IsSuccess.Should().BeTrue();
        upcomingRes.Value.TotalCount.Should().Be(1);
        upcomingRes.Value.Items.Should().ContainSingle(m => m.Id == upcomingMatch.Id);

        // Act & Assert History
        var historyRes = await historyHandler.HandleAsync(new GetMatchHistoryQuery(TeamId: teamA));
        historyRes.IsSuccess.Should().BeTrue();
        historyRes.Value.TotalCount.Should().Be(1);
        historyRes.Value.Items.Should().ContainSingle(m => m.Id == cancelledMatch.Id && m.Status == "Cancelled");

        // Act & Assert GetById
        var getByIdRes = await getByIdHandler.HandleAsync(new GetMatchByIdQuery(upcomingMatch.Id));
        getByIdRes.IsSuccess.Should().BeTrue();
        getByIdRes.Value.HomeTeamName.Should().Be("Team Alpha");
        getByIdRes.Value.AwayTeamName.Should().Be("Team Beta");
    }

    [Fact]
    public async Task UpcomingQuery_KeepsRecentlyFinishedMatchesAwaitingAResultAsync()
    {
        using var db = CreateInMemoryDb();
        var captaincy = new FakeCaptaincyChecker();
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        captaincy.RegisterTeam(teamA, "Team Alpha", Guid.NewGuid());
        captaincy.RegisterTeam(teamB, "Team Beta", Guid.NewGuid());

        var awaitingResult = Match.Schedule(
            null, teamA, teamB, _clock.UtcNow.AddDays(-3), 60, null, null, null, null, null, _clock.UtcNow.AddDays(-4)).Value;
        var stale = Match.Schedule(
            null, teamA, teamB, _clock.UtcNow.AddDays(-9), 60, null, null, null, null, null, _clock.UtcNow.AddDays(-10)).Value;
        db.Matches.AddRange(awaitingResult, stale);
        await db.SaveChangesAsync();

        var result = await new GetUpcomingMatchesHandler(db, captaincy, _clock)
            .HandleAsync(new GetUpcomingMatchesQuery(TeamId: teamA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(m => m.Id == awaitingResult.Id);
    }

    [Fact]
    public async Task RespondToChallenge_Accept_ShouldCreateMatch_AndCheckConflictsAsync()
    {
        using var db = CreateInMemoryDb();
        var mockCaptaincy = new FakeCaptaincyChecker();
        var mockNotifications = new FakeNotificationPublisher();

        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        mockCaptaincy.RegisterTeam(teamA, "Team A", captainA);
        mockCaptaincy.RegisterTeam(teamB, "Team B", captainB);

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(),
            teamA,
            teamB,
            captainA,
            _clock.UtcNow.AddDays(3),
            _clock.UtcNow,
            60,
            proposedVenueName: "City Arena").Value;

        db.TeamChallenges.Add(challenge);
        await db.SaveChangesAsync();

        var respondHandler = new RespondToChallengeHandler(db, mockCaptaincy, mockNotifications, _clock);

        // Act: Captain B accepts challenge
        var acceptRes = await respondHandler.HandleAsync(new RespondToChallengeCommand(
            challenge.Id,
            teamB,
            captainB,
            ChallengeResponseAction.Accept));

        acceptRes.IsSuccess.Should().BeTrue();

        // Verify match was created for the accepted challenge
        var match = await db.Matches.FirstOrDefaultAsync(m => m.ChallengeId == challenge.Id);
        match.Should().NotBeNull();
        match!.HomeTeamId.Should().Be(teamA);
        match.AwayTeamId.Should().Be(teamB);
        match.Status.Should().Be(MatchStatus.Scheduled);
        match.DurationMinutes.Should().Be(60);

        // Notification sent to challenger captain
        mockNotifications.Published.Should().ContainSingle(n => n.UserId == captainA && n.Type == NotificationTypes.MatchScheduled);

        // Now test link challenge booking links both challenge and match
        var linkHandler = new LinkChallengeBookingHandler(db, mockCaptaincy, _clock);
        var bookingId = Guid.NewGuid();
        var linkRes = await linkHandler.HandleAsync(new LinkChallengeBookingCommand(challenge.Id, teamA, captainA, bookingId));
        linkRes.IsSuccess.Should().BeTrue();

        var reloadedMatch = await db.Matches.FirstOrDefaultAsync(m => m.ChallengeId == challenge.Id);
        reloadedMatch!.BookingId.Should().Be(bookingId);
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
