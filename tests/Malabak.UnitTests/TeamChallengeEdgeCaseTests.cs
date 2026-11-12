using FluentAssertions;
using Malabak.Modules.Matches.Application.Challenges.LinkBooking;
using Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;
using Malabak.Modules.Matches.Application.Challenges.SendChallenge;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class TeamChallengeEdgeCaseTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData(29)]
    [InlineData(0)]
    [InlineData(-15)]
    [InlineData(181)]
    [InlineData(300)]
    public void TeamChallenge_Create_WithInvalidDuration_ShouldBeRejected(int durationMinutes)
    {
        // Act
        var result = TeamChallenge.Create(
            Guid.NewGuid(),
            challengerTeamId: Guid.NewGuid(),
            challengedTeamId: Guid.NewGuid(),
            challengerCaptainId: Guid.NewGuid(),
            proposedDateUtc: _clock.UtcNow.AddDays(1),
            nowUtc: _clock.UtcNow,
            durationMinutes: durationMinutes);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChallengeErrors.InvalidDuration);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(180)]
    public void TeamChallenge_Create_WithValidBoundaryDuration_ShouldSucceed(int durationMinutes)
    {
        // Act
        var result = TeamChallenge.Create(
            Guid.NewGuid(),
            challengerTeamId: Guid.NewGuid(),
            challengedTeamId: Guid.NewGuid(),
            challengerCaptainId: Guid.NewGuid(),
            proposedDateUtc: _clock.UtcNow.AddDays(2),
            nowUtc: _clock.UtcNow,
            durationMinutes: durationMinutes);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ProposedDurationMinutes.Should().Be(durationMinutes);
    }

    [Fact]
    public void TeamChallenge_Accept_WhenProposedDateHasPassed_ShouldFailWithChallengeExpired()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var proposedDate = _clock.UtcNow.AddHours(2);

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, proposedDate, _clock.UtcNow).Value;

        // Advance clock past the proposed kickoff time
        _clock.Advance(TimeSpan.FromHours(3));

        // Act: Team B attempts to accept an already passed match date
        var result = challenge.Accept(teamB, captainB, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChallengeErrors.ChallengeExpired);
        challenge.Status.Should().Be(ChallengeStatus.Pending);
    }

    [Fact]
    public void TeamChallenge_Accept_ByChallengerTeam_ShouldFailWithNotYourTurn()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(1), _clock.UtcNow).Value;

        // Act: Challenger team tries to accept its own challenge before opponent responds
        var result = challenge.Accept(teamA, captainA, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChallengeErrors.NotYourTurn);
    }

    [Fact]
    public void TeamChallenge_CounterPropose_PastDate_ShouldBeRejected()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        // Act: Team B counter-proposes a time in the past
        var result = challenge.CounterPropose(teamB, captainB, _clock.UtcNow.AddHours(-1), _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChallengeErrors.DateInPast);
    }

    [Fact]
    public void TeamChallenge_CounterPropose_WhenNotTurn_ShouldBeRejected()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        // Act: Team A counter-proposes immediately without waiting for Team B
        var result = challenge.CounterPropose(teamA, captainA, _clock.UtcNow.AddDays(3), _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChallengeErrors.NotYourTurn);
    }

    [Fact]
    public void TeamChallenge_Cancel_ByReceivingTeam_ShouldBeRejected()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        // Act: Team B (the receiving team) tries to Cancel instead of Decline
        var result = challenge.Cancel(teamB, captainB, _clock.UtcNow);

        // Assert: Only the team that made the offer can cancel
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChallengeErrors.NotAuthorizedCaptain);
    }

    [Fact]
    public void TeamChallenge_Cancel_ByOfferingTeam_ShouldSucceed()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        // Act: Challenger team cancels before any response
        var result = challenge.Cancel(teamA, captainA, _clock.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        challenge.Status.Should().Be(ChallengeStatus.Cancelled);
    }

    [Fact]
    public void TeamChallenge_Decline_ByUnrelatedThirdParty_ShouldBeRejected()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var teamC = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainC = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        // Act: Team C attempts to decline
        var result = challenge.Decline(teamC, captainC, "Not our game", _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChallengeErrors.NotAuthorizedCaptain);
    }

    [Fact]
    public void TeamChallenge_LinkBooking_WhenNotAccepted_ShouldFailWithInvalidStatus()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        // Act: Attempt to link booking while status is still Pending
        var result = challenge.LinkBooking(Guid.NewGuid(), teamA, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChallengeErrors.InvalidStatus);
    }

    [Fact]
    public void TeamChallenge_LinkBooking_WithEmptyGuid_ShouldFailValidation()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        challenge.Accept(teamB, captainB, _clock.UtcNow);

        // Act: Attempt to link with Guid.Empty
        var result = challenge.LinkBooking(Guid.Empty, teamA, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Challenge.InvalidBookingId");
    }

    [Fact]
    public void TeamChallenge_LinkBooking_ByThirdPartyTeam_ShouldBeRejected()
    {
        // Arrange
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var teamC = Guid.NewGuid();
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var challenge = TeamChallenge.Create(
            Guid.NewGuid(), teamA, teamB, captainA, _clock.UtcNow.AddDays(2), _clock.UtcNow).Value;

        challenge.Accept(teamB, captainB, _clock.UtcNow);

        // Act: Team C tries to link booking
        var result = challenge.LinkBooking(Guid.NewGuid(), teamC, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChallengeErrors.NotAuthorizedCaptain);
    }

    [Fact]
    public void TeamChallenge_DeepLink_ShouldContainExpectedParameters()
    {
        // Arrange
        var challengeId = Guid.NewGuid();
        var pitchId = Guid.NewGuid();
        var venueId = Guid.NewGuid();
        var proposedDate = _clock.UtcNow.AddDays(3);

        var challenge = TeamChallenge.Create(
            challengeId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            proposedDate,
            _clock.UtcNow,
            durationMinutes: 90,
            proposedPitchId: pitchId,
            proposedVenueId: venueId).Value;

        // Act
        var deepLink = challenge.GetBookingDeepLink();

        // Assert
        deepLink.Should().StartWith("malabak://booking/create?");
        deepLink.Should().Contain($"challengeId={challengeId:D}");
        deepLink.Should().Contain($"venueId={venueId:D}");
        deepLink.Should().Contain($"pitchId={pitchId:D}");
        deepLink.Should().Contain("duration=90");
    }
}
