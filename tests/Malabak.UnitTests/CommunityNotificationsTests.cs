using FluentAssertions;
using Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;
using Malabak.Modules.Matches.Application.Challenges.SendChallenge;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.Modules.Notifications.Domain;
using Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Teams.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Ranking;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class CommunityNotificationsTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc));

    private static MatchesDbContext CreateInMemoryMatchesDb()
    {
        var options = new DbContextOptionsBuilder<MatchesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MatchesDbContext(options);
    }

    private static TeamsDbContext CreateInMemoryTeamsDb()
    {
        var options = new DbContextOptionsBuilder<TeamsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TeamsDbContext(options);
    }

    private sealed class CapturingNotificationPublisher : INotificationPublisher
    {
        public List<NotificationRequest> Requests { get; } = [];

        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTeamCaptaincyChecker : ITeamCaptaincyChecker
    {
        public Dictionary<Guid, Guid> CaptainsByTeam { get; } = [];
        public Dictionary<Guid, string> TeamNames { get; } = [];

        public Task<bool> IsCaptainOrViceCaptainAsync(Guid teamId, Guid playerId, CancellationToken cancellationToken = default)
            => Task.FromResult(CaptainsByTeam.TryGetValue(teamId, out var captain) && captain == playerId);

        public Task<Guid?> GetCaptainPlayerIdAsync(Guid teamId, CancellationToken cancellationToken = default)
            => Task.FromResult(CaptainsByTeam.TryGetValue(teamId, out var captain) ? (Guid?)captain : null);

        public Task<bool> CanBeChallengedAsync(Guid teamId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string?> GetTeamNameAsync(Guid teamId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(TeamNames.TryGetValue(teamId, out var name) ? name : "Test Team");
    }

    [Theory]
    [InlineData(NotificationTypes.TeamInvitationReceived, "malabak://team/11111111-1111-1111-1111-111111111111")]
    [InlineData(NotificationTypes.TeamChallengeReceived, "malabak://challenge/22222222-2222-2222-2222-222222222222")]
    [InlineData(NotificationTypes.TeamChallengeAccepted, "malabak://match/33333333-3333-3333-3333-333333333333")]
    [InlineData(NotificationTypes.TeamChallengeDeclined, "malabak://team/11111111-1111-1111-1111-111111111111")]
    [InlineData(NotificationTypes.TeamChallengeCountered, "malabak://challenge/22222222-2222-2222-2222-222222222222")]
    [InlineData(NotificationTypes.TeamRankingUpdated, "malabak://team/11111111-1111-1111-1111-111111111111")]
    [InlineData(NotificationTypes.MatchScheduled, "malabak://match/33333333-3333-3333-3333-333333333333")]
    [InlineData(NotificationTypes.MatchResultSubmitted, "malabak://match/33333333-3333-3333-3333-333333333333")]
    [InlineData(NotificationTypes.MatchResultConfirmed, "malabak://match/33333333-3333-3333-3333-333333333333")]
    [InlineData(NotificationTypes.MatchResultDisputed, "malabak://match/33333333-3333-3333-3333-333333333333")]
    [InlineData(NotificationTypes.MatchDisputeResolved, "malabak://match/33333333-3333-3333-3333-333333333333")]
    public void CommunityNotificationTypes_RenderArabicAndEnglish_WithValidDeepLinks(string notificationType, string expectedDeepLink)
    {
        // Arrange
        var data = new Dictionary<string, string>
        {
            ["teamId"] = "11111111-1111-1111-1111-111111111111",
            ["teamName"] = "الأهلي",
            ["challengeId"] = "22222222-2222-2222-2222-222222222222",
            ["matchId"] = "33333333-3333-3333-3333-333333333333",
            ["matchDate"] = "2026-11-10 18:00",
            ["newDate"] = "2026-11-11 20:00",
            ["inviteCode"] = "ABCXYZ",
            ["submittingTeam"] = "الزمالك",
            ["disputingTeam"] = "الزمالك",
            ["score"] = "3 - 2",
            ["reason"] = "Weather condition",
            ["decision"] = "Confirmed submitted score",
            ["newRating"] = "1650"
        };

        // Act
        var found = NotificationCatalog.TryGet(notificationType, out var template);

        // Assert
        found.Should().BeTrue($"template for {notificationType} must exist in catalog");
        template.Category.Should().Be(NotificationCategory.Teams);

        template.TitleAr.Should().NotBeNullOrWhiteSpace();
        template.BodyAr.Should().NotBeNullOrWhiteSpace();
        template.TitleEn.Should().NotBeNullOrWhiteSpace();
        template.BodyEn.Should().NotBeNullOrWhiteSpace();

        var renderedAr = NotificationCatalog.Render(template.BodyAr, data);
        renderedAr.Should().NotContain("{", "all placeholders should be filled");

        var renderedEn = NotificationCatalog.Render(template.BodyEn, data);
        renderedEn.Should().NotContain("{", "all placeholders should be filled");

        var link = template.DeepLink(data);
        link.Should().Be(expectedDeepLink);
    }

    [Fact]
    public async Task SendChallenge_DispatchesPushNotification_ToDefendingCaptain_WithDedupeKeyAsync()
    {
        // Arrange
        using var db = CreateInMemoryMatchesDb();
        var checker = new FakeTeamCaptaincyChecker();
        var publisher = new CapturingNotificationPublisher();
        var handler = new SendChallengeHandler(db, checker, publisher, _clock);

        var challengerTeamId = Guid.NewGuid();
        var challengedTeamId = Guid.NewGuid();
        var challengerCaptainId = Guid.NewGuid();
        var challengedCaptainId = Guid.NewGuid();

        checker.CaptainsByTeam[challengerTeamId] = challengerCaptainId;
        checker.CaptainsByTeam[challengedTeamId] = challengedCaptainId;
        checker.TeamNames[challengerTeamId] = "Red Devils";
        checker.TeamNames[challengedTeamId] = "Blue Knights";

        var command = new SendChallengeCommand(
            ChallengerTeamId: challengerTeamId,
            ChallengedTeamId: challengedTeamId,
            CallerPlayerId: challengerCaptainId,
            ProposedDateUtc: _clock.UtcNow.AddDays(3),
            DurationMinutes: 60,
            ProposedPitchId: null,
            ProposedVenueId: null,
            ProposedVenueName: null,
            Notes: "Friendly challenge");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        publisher.Requests.Should().ContainSingle();

        var notif = publisher.Requests[0];
        notif.UserId.Should().Be(challengedCaptainId);
        notif.Type.Should().Be(NotificationTypes.TeamChallengeReceived);
        notif.Data["teamName"].Should().Be("Red Devils");
        notif.Data["challengeId"].Should().Be(result.Value.Id.ToString("D"));
        notif.DedupeKey.Should().Be($"challenge:{result.Value.Id}");
    }

    [Fact]
    public async Task RespondToChallenge_Accept_DispatchesChallengeAcceptedAndMatchScheduledNotificationsAsync()
    {
        // Arrange
        using var db = CreateInMemoryMatchesDb();
        var checker = new FakeTeamCaptaincyChecker();
        var publisher = new CapturingNotificationPublisher();
        var handler = new RespondToChallengeHandler(db, checker, publisher, _clock);

        var challengerTeamId = Guid.NewGuid();
        var challengedTeamId = Guid.NewGuid();
        var challengerCaptainId = Guid.NewGuid();
        var challengedCaptainId = Guid.NewGuid();

        checker.CaptainsByTeam[challengerTeamId] = challengerCaptainId;
        checker.CaptainsByTeam[challengedTeamId] = challengedCaptainId;
        checker.TeamNames[challengerTeamId] = "Tigers";
        checker.TeamNames[challengedTeamId] = "Lions";

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(),
            challengerTeamId,
            challengedTeamId,
            challengerCaptainId,
            _clock.UtcNow.AddDays(2),
            _clock.UtcNow).Value;

        db.TeamChallenges.Add(challenge);
        await db.SaveChangesAsync();

        var command = new RespondToChallengeCommand(
            ChallengeId: challenge.Id,
            RespondingTeamId: challengedTeamId,
            CallerPlayerId: challengedCaptainId,
            Action: ChallengeResponseAction.Accept);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        publisher.Requests.Should().HaveCount(2);

        var acceptNotif = publisher.Requests.First(r => r.Type == NotificationTypes.TeamChallengeAccepted);
        acceptNotif.UserId.Should().Be(challengerCaptainId);
        acceptNotif.Data["teamName"].Should().Be("Lions");

        var schedNotif = publisher.Requests.First(r => r.Type == NotificationTypes.MatchScheduled);
        schedNotif.UserId.Should().Be(challengerCaptainId);
    }

    [Fact]
    public async Task RespondToChallenge_Decline_DispatchesChallengeDeclinedNotificationAsync()
    {
        // Arrange
        using var db = CreateInMemoryMatchesDb();
        var checker = new FakeTeamCaptaincyChecker();
        var publisher = new CapturingNotificationPublisher();
        var handler = new RespondToChallengeHandler(db, checker, publisher, _clock);

        var challengerTeamId = Guid.NewGuid();
        var challengedTeamId = Guid.NewGuid();
        var challengerCaptainId = Guid.NewGuid();
        var challengedCaptainId = Guid.NewGuid();

        checker.CaptainsByTeam[challengerTeamId] = challengerCaptainId;
        checker.CaptainsByTeam[challengedTeamId] = challengedCaptainId;
        checker.TeamNames[challengerTeamId] = "Hawks";
        checker.TeamNames[challengedTeamId] = "Falcons";

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(),
            challengerTeamId,
            challengedTeamId,
            challengerCaptainId,
            _clock.UtcNow.AddDays(2),
            _clock.UtcNow).Value;

        db.TeamChallenges.Add(challenge);
        await db.SaveChangesAsync();

        var command = new RespondToChallengeCommand(
            ChallengeId: challenge.Id,
            RespondingTeamId: challengedTeamId,
            CallerPlayerId: challengedCaptainId,
            Action: ChallengeResponseAction.Decline,
            DeclineReason: "Pitch maintenance scheduled that day");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        publisher.Requests.Should().ContainSingle();

        var declineNotif = publisher.Requests[0];
        declineNotif.UserId.Should().Be(challengerCaptainId);
        declineNotif.Type.Should().Be(NotificationTypes.TeamChallengeDeclined);
        declineNotif.Data["teamName"].Should().Be("Falcons");
        declineNotif.Data["reason"].Should().Be("Pitch maintenance scheduled that day");
        declineNotif.DedupeKey.Should().Be($"challenge-decline:{challenge.Id}");
    }

    [Fact]
    public async Task RespondToChallenge_CounterPropose_DispatchesChallengeCounteredNotificationAsync()
    {
        // Arrange
        using var db = CreateInMemoryMatchesDb();
        var checker = new FakeTeamCaptaincyChecker();
        var publisher = new CapturingNotificationPublisher();
        var handler = new RespondToChallengeHandler(db, checker, publisher, _clock);

        var challengerTeamId = Guid.NewGuid();
        var challengedTeamId = Guid.NewGuid();
        var challengerCaptainId = Guid.NewGuid();
        var challengedCaptainId = Guid.NewGuid();

        checker.CaptainsByTeam[challengerTeamId] = challengerCaptainId;
        checker.CaptainsByTeam[challengedTeamId] = challengedCaptainId;
        checker.TeamNames[challengerTeamId] = "Stars";
        checker.TeamNames[challengedTeamId] = "Moons";

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(),
            challengerTeamId,
            challengedTeamId,
            challengerCaptainId,
            _clock.UtcNow.AddDays(2),
            _clock.UtcNow).Value;

        db.TeamChallenges.Add(challenge);
        await db.SaveChangesAsync();

        var newDate = _clock.UtcNow.AddDays(4);
        var command = new RespondToChallengeCommand(
            ChallengeId: challenge.Id,
            RespondingTeamId: challengedTeamId,
            CallerPlayerId: challengedCaptainId,
            Action: ChallengeResponseAction.CounterPropose,
            NewProposedDateUtc: newDate);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        publisher.Requests.Should().ContainSingle();

        var counterNotif = publisher.Requests[0];
        counterNotif.UserId.Should().Be(challengerCaptainId);
        counterNotif.Type.Should().Be(NotificationTypes.TeamChallengeCountered);
        counterNotif.Data["teamName"].Should().Be("Moons");
        counterNotif.Data["newDate"].Should().Be(newDate.ToString("yyyy-MM-dd HH:mm"));
    }

    [Fact]
    public async Task CreateTeamInvitation_WithInvitedPlayerId_DispatchesTeamInvitationNotificationAsync()
    {
        // Arrange
        using var db = CreateInMemoryTeamsDb();
        var publisher = new CapturingNotificationPublisher();
        var handler = new CreateTeamInvitationHandler(db, _clock, new TeamInviteCodeGenerator(), publisher);

        var captainId = Guid.NewGuid();
        var invitedPlayerId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Zamalek Youth", "zamalek-youth", "Giza", "Giza", captainId, "123456", _clock.UtcNow).Value;

        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var command = new CreateTeamInvitationCommand(
            TeamId: team.Id,
            RequestingPlayerId: captainId,
            ValidityDays: 7,
            InvitedPlayerId: invitedPlayerId);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        publisher.Requests.Should().ContainSingle();

        var notif = publisher.Requests[0];
        notif.UserId.Should().Be(invitedPlayerId);
        notif.Type.Should().Be(NotificationTypes.TeamInvitationReceived);
        notif.Data["teamName"].Should().Be("Zamalek Youth");
        notif.Data["inviteCode"].Should().Be(result.Value.InviteCode);
        notif.DedupeKey.Should().Be($"team-invitation:{team.Id}:{invitedPlayerId}");
    }

    [Fact]
    public async Task TeamMatchResultRecorder_WhenConfirmed_DispatchesTeamRankingUpdatedNotificationsAsync()
    {
        // Arrange
        using var db = CreateInMemoryTeamsDb();
        var publisher = new CapturingNotificationPublisher();
        var recorder = new TeamMatchResultRecorder(db, new Glicko2RankingStrategy(), _clock, publisher);

        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var teamA = Team.Create(Guid.NewGuid(), "Ahly SC", "ahly-sc", "Cairo", "Cairo", captainA, "111111", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Pyramids FC", "pyramids-fc", "Cairo", "Cairo", captainB, "222222", _clock.UtcNow).Value;

        db.Teams.AddRange(teamA, teamB);
        await db.SaveChangesAsync();

        // Act
        await recorder.RecordConfirmedResultAsync(teamA.Id, teamB.Id, 2, 1);

        // Assert
        publisher.Requests.Should().HaveCount(2);

        var notifA = publisher.Requests.First(r => r.UserId == captainA);
        notifA.Type.Should().Be(NotificationTypes.TeamRankingUpdated);
        notifA.Data["teamName"].Should().Be("Ahly SC");

        var notifB = publisher.Requests.First(r => r.UserId == captainB);
        notifB.Type.Should().Be(NotificationTypes.TeamRankingUpdated);
        notifB.Data["teamName"].Should().Be("Pyramids FC");
    }
}
