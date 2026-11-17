using FluentAssertions;
using Malabak.Modules.Matches.Application.Challenges.GetChallengeById;
using Malabak.Modules.Matches.Application.Challenges.GetTeamChallenges;
using Malabak.Modules.Matches.Application.Challenges.LinkBooking;
using Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;
using Malabak.Modules.Matches.Application.Challenges.SendChallenge;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Domain.Events;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class TeamChallengeTests
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
    public void TeamChallenge_Create_ShouldInitializeCorrectly_AndRaiseEvent()
    {
        // Arrange
        var challengeId = Guid.NewGuid();
        var challengerTeamId = Guid.NewGuid();
        var challengedTeamId = Guid.NewGuid();
        var captainId = Guid.NewGuid();
        var proposedDate = _clock.UtcNow.AddDays(2);

        // Act
        var result = TeamChallenge.Create(
            challengeId,
            challengerTeamId,
            challengedTeamId,
            captainId,
            proposedDate,
            _clock.UtcNow,
            durationMinutes: 90,
            proposedVenueName: "Al Ahly Club",
            notes: "Friendly 7v7 match");

        // Assert
        result.IsSuccess.Should().BeTrue();
        var challenge = result.Value;
        challenge.Id.Should().Be(challengeId);
        challenge.ChallengerTeamId.Should().Be(challengerTeamId);
        challenge.ChallengedTeamId.Should().Be(challengedTeamId);
        challenge.ChallengerCaptainId.Should().Be(captainId);
        challenge.ProposedDateUtc.Should().Be(proposedDate);
        challenge.ProposedDurationMinutes.Should().Be(90);
        challenge.Status.Should().Be(ChallengeStatus.Pending);
        challenge.CurrentTurnTeamId.Should().Be(challengedTeamId);
        challenge.ProposedVenueName.Should().Be("Al Ahly Club");
        challenge.Notes.Should().Be("Friendly 7v7 match");

        challenge.DomainEvents.Should().ContainSingle(e => e is ChallengeSentDomainEvent);
    }

    [Fact]
    public void TeamChallenge_Create_WithInvalidData_ShouldFail()
    {
        var teamId = Guid.NewGuid();
        var captainId = Guid.NewGuid();

        // 1. Cannot challenge self
        var selfChallenge = TeamChallenge.Create(
            Guid.NewGuid(), teamId, teamId, captainId, _clock.UtcNow.AddDays(1), _clock.UtcNow);
        selfChallenge.IsSuccess.Should().BeFalse();
        selfChallenge.Error.Should().Be(ChallengeErrors.CannotChallengeSelf);

        // 2. Date in past
        var pastChallenge = TeamChallenge.Create(
            Guid.NewGuid(), teamId, Guid.NewGuid(), captainId, _clock.UtcNow.AddDays(-1), _clock.UtcNow);
        pastChallenge.IsSuccess.Should().BeFalse();
        pastChallenge.Error.Should().Be(ChallengeErrors.DateInPast);

        // 3. Duration too short or too long
        var shortDuration = TeamChallenge.Create(
            Guid.NewGuid(), teamId, Guid.NewGuid(), captainId, _clock.UtcNow.AddDays(1), _clock.UtcNow, durationMinutes: 15);
        shortDuration.IsSuccess.Should().BeFalse();
        shortDuration.Error.Should().Be(ChallengeErrors.InvalidDuration);

        var longDuration = TeamChallenge.Create(
            Guid.NewGuid(), teamId, Guid.NewGuid(), captainId, _clock.UtcNow.AddDays(1), _clock.UtcNow, durationMinutes: 200);
        longDuration.IsSuccess.Should().BeFalse();
        longDuration.Error.Should().Be(ChallengeErrors.InvalidDuration);
    }

    [Fact]
    public void TeamChallenge_CounterPropose_ShouldUpdateDetailsAndSwitchTurn()
    {
        // Arrange
        var challengeId = Guid.NewGuid();
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            challengeId, teamA, teamB, captainA, _clock.UtcNow.AddDays(3), _clock.UtcNow).Value;

        // Act 1: Team A tries to counter when it's Team B's turn -> fails
        var notYourTurn = challenge.CounterPropose(teamA, captainA, _clock.UtcNow.AddDays(4), _clock.UtcNow);
        notYourTurn.IsSuccess.Should().BeFalse();
        notYourTurn.Error.Should().Be(ChallengeErrors.NotYourTurn);

        // Act 2: Team B counters with new date
        var newDate = _clock.UtcNow.AddDays(5);
        var counterRes = challenge.CounterPropose(teamB, captainB, newDate, _clock.UtcNow, 90, newProposedVenueName: "Wadi Degla");

        // Assert
        counterRes.IsSuccess.Should().BeTrue();
        challenge.Status.Should().Be(ChallengeStatus.CounterProposed);
        challenge.ProposedDateUtc.Should().Be(newDate);
        challenge.ProposedDurationMinutes.Should().Be(90);
        challenge.ProposedVenueName.Should().Be("Wadi Degla");
        challenge.CurrentTurnTeamId.Should().Be(teamA, "turn must switch back to Team A");
        challenge.DomainEvents.Should().Contain(e => e is ChallengeCounterProposedDomainEvent);
    }

    [Fact]
    public void TeamChallenge_Accept_ShouldSucceed_AndNotBookPitch()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        // Act 1: Team A tries to accept own proposal -> fails
        var invalidTurn = challenge.Accept(teamA, captainA, _clock.UtcNow);
        invalidTurn.IsSuccess.Should().BeFalse();
        invalidTurn.Error.Should().Be(ChallengeErrors.NotYourTurn);

        // Act 2: Team B accepts
        var acceptRes = challenge.Accept(teamB, captainB, _clock.UtcNow);

        // Assert
        acceptRes.IsSuccess.Should().BeTrue();
        challenge.Status.Should().Be(ChallengeStatus.Accepted);
        challenge.AcceptedAtUtc.Should().Be(_clock.UtcNow);
        challenge.MatchId.Should().BeNull("challenge acceptance does not automatically book a pitch");
        challenge.DomainEvents.Should().Contain(e => e is ChallengeAcceptedDomainEvent);
    }

    [Fact]
    public void TeamChallenge_DeclineAndCancel_ShouldTransitionProperly()
    {
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        // 1. Decline
        var challenge1 = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        var declineRes = challenge1.Decline(teamB, captainB, "Roster unavailable", _clock.UtcNow);
        declineRes.IsSuccess.Should().BeTrue();
        challenge1.Status.Should().Be(ChallengeStatus.Declined);
        challenge1.DeclinedReason.Should().Be("Roster unavailable");

        // 2. Cancel by proposing team
        var challenge2 = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        // Team B tries to cancel A's initial proposal -> fails
        var invalidCancel = challenge2.Cancel(teamB, captainB, _clock.UtcNow);
        invalidCancel.IsSuccess.Should().BeFalse();
        invalidCancel.Error.Should().Be(ChallengeErrors.NotAuthorizedCaptain);

        // Team A cancels own proposal -> succeeds
        var cancelRes = challenge2.Cancel(teamA, captainA, _clock.UtcNow);
        cancelRes.IsSuccess.Should().BeTrue();
        challenge2.Status.Should().Be(ChallengeStatus.Cancelled);
    }

    [Fact]
    public async Task Challenge_Handlers_SendRespondAndQuery_FullLifecycleAsync()
    {
        await using var db = CreateInMemoryDb();
        var mockCaptaincy = new FakeCaptaincyChecker();
        var mockNotifications = new FakeNotificationPublisher();

        var sendHandler = new SendChallengeHandler(db, mockCaptaincy, mockNotifications, _clock);
        var respondHandler = new RespondToChallengeHandler(db, mockCaptaincy, mockNotifications, _clock);
        var getByIdHandler = new GetChallengeByIdHandler(db, mockCaptaincy);
        var getTeamChallengesHandler = new GetTeamChallengesHandler(db, mockCaptaincy);

        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        mockCaptaincy.RegisterTeam(teamA, "Cairo Warriors", captainA);
        mockCaptaincy.RegisterTeam(teamB, "Giza Phantoms", captainB);

        // Step 1: Team A sends challenge to Team B
        var sendCmd = new SendChallengeCommand(
            teamA,
            teamB,
            captainA,
            _clock.UtcNow.AddDays(3),
            60,
            null,
            null,
            "Rehab Stadium",
            "Friendly match");

        var sendRes = await sendHandler.HandleAsync(sendCmd);
        sendRes.IsSuccess.Should().BeTrue();
        var challengeId = sendRes.Value.Id;
        sendRes.Value.ChallengerTeamName.Should().Be("Cairo Warriors");
        sendRes.Value.ChallengedTeamName.Should().Be("Giza Phantoms");
        sendRes.Value.Status.Should().Be(ChallengeStatus.Pending);

        // Notification must have been dispatched to Team B captain
        mockNotifications.Published.Should().ContainSingle(n => n.UserId == captainB && n.Type == NotificationTypes.TeamChallengeReceived);

        // Step 2: Team B queries their challenges
        var teamChallengesRes = await getTeamChallengesHandler.HandleAsync(new GetTeamChallengesQuery(teamB, captainB));
        teamChallengesRes.IsSuccess.Should().BeTrue();
        teamChallengesRes.Value.Should().ContainSingle(c => c.Id == challengeId && c.IsMyTurn);

        // Step 3: Team B counter-proposes with new date
        var counterCmd = new RespondToChallengeCommand(
            challengeId,
            teamB,
            captainB,
            ChallengeResponseAction.CounterPropose,
            NewProposedDateUtc: _clock.UtcNow.AddDays(4),
            NewDurationMinutes: 90,
            NewProposedVenueName: "Shooting Club");

        var counterRes = await respondHandler.HandleAsync(counterCmd);
        counterRes.IsSuccess.Should().BeTrue();
        counterRes.Value.Status.Should().Be(ChallengeStatus.CounterProposed);
        counterRes.Value.ProposedDurationMinutes.Should().Be(90);

        // Step 4: Team A accepts the counter proposal
        var acceptCmd = new RespondToChallengeCommand(
            challengeId,
            teamA,
            captainA,
            ChallengeResponseAction.Accept);

        var acceptRes = await respondHandler.HandleAsync(acceptCmd);
        acceptRes.IsSuccess.Should().BeTrue();
        acceptRes.Value.Status.Should().Be(ChallengeStatus.Accepted);
        acceptRes.Value.AcceptedAtUtc.Should().NotBeNull();
        acceptRes.Value.MatchId.Should().NotBeNull("accepting schedules the match and the challenge links to it");
        acceptRes.Value.BookingId.Should().BeNull("challenge acceptance does not automatically book a pitch");

        // Step 5: Query challenge by ID
        var detailRes = await getByIdHandler.HandleAsync(new GetChallengeByIdQuery(challengeId, captainA));
        detailRes.IsSuccess.Should().BeTrue();
        detailRes.Value.Status.Should().Be(ChallengeStatus.Accepted);
        detailRes.Value.BookingDeepLink.Should().NotBeNull();
        detailRes.Value.BookingDeepLink.Should().StartWith("malabak://booking/create?challengeId=");
    }

    [Fact]
    public async Task ChallengeToBookingLinking_IndependentStatusesAndDeepLink_FlowAsync()
    {
        await using var db = CreateInMemoryDb();
        var mockCaptaincy = new FakeCaptaincyChecker();

        var linkHandler = new LinkChallengeBookingHandler(db, mockCaptaincy, _clock);

        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var venueId = Guid.NewGuid();
        var pitchId = Guid.NewGuid();

        mockCaptaincy.RegisterTeam(teamA, "Alpha FC", captainA);
        mockCaptaincy.RegisterTeam(teamB, "Beta FC", captainB);

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow,
            proposedVenueId: venueId, proposedPitchId: pitchId, proposedVenueName: "City Arena").Value;

        // Challenge is Pending
        challenge.GetBookingDeepLink().Should().Contain($"venueId={venueId}").And.Contain($"pitchId={pitchId}");
        challenge.BookingId.Should().BeNull();

        db.TeamChallenges.Add(challenge);
        await db.SaveChangesAsync();

        // 1. Linking booking before acceptance must fail
        var preAcceptLink = await linkHandler.HandleAsync(new LinkChallengeBookingCommand(challenge.Id, teamA, captainA, Guid.NewGuid()));
        preAcceptLink.IsSuccess.Should().BeFalse();
        preAcceptLink.Error.Should().Be(ChallengeErrors.InvalidStatus);

        // 2. Accept challenge (acceptance does NOT automatically create booking)
        challenge.Accept(teamB, captainB, _clock.UtcNow);
        await db.SaveChangesAsync();
        challenge.BookingId.Should().BeNull("acceptance must never automatically book a pitch");

        // 3. Unauthorized captain cannot link
        var unauthorizedLink = await linkHandler.HandleAsync(new LinkChallengeBookingCommand(challenge.Id, teamA, Guid.NewGuid(), Guid.NewGuid()));
        unauthorizedLink.IsSuccess.Should().BeFalse();
        unauthorizedLink.Error.Should().Be(ChallengeErrors.NotAuthorizedCaptain);

        // 4. Captain links a created booking
        var bookingId = Guid.NewGuid();
        var linkRes = await linkHandler.HandleAsync(new LinkChallengeBookingCommand(challenge.Id, teamA, captainA, bookingId));
        linkRes.IsSuccess.Should().BeTrue();
        linkRes.Value.BookingId.Should().Be(bookingId);
        linkRes.Value.Status.Should().Be(ChallengeStatus.Accepted, "challenge status remains Accepted independently");
        linkRes.Value.BookingDeepLink.Should().Contain($"challengeId={challenge.Id}");
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
