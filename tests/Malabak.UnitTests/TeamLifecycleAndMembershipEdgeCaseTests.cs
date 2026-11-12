using FluentAssertions;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Application.Teams.CreateTeam;
using Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Teams.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class TeamLifecycleAndMembershipEdgeCaseTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc));

    private static TeamsDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<TeamsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TeamsDbContext(options);
    }

    [Fact]
    public void CreateTeam_WithArabicNameAndValidFields_ShouldSucceedAndInitializeDefaults()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var captainId = Guid.NewGuid();
        var arabicName = "نجوم الدقي لكرة القدم";

        // Act
        var result = Team.Create(
            teamId,
            name: arabicName,
            slug: "nogoum-el-dokki",
            city: "Giza",
            governorate: "Giza",
            captainId: captainId,
            inviteCode: "DOKK01",
            nowUtc: _clock.UtcNow,
            description: "فريق شباب الدقي للمباريات الودية والبطولات",
            district: "Dokki");

        // Assert
        result.IsSuccess.Should().BeTrue();
        var team = result.Value;
        team.Name.Should().Be(arabicName);
        team.Rating.Should().Be(1500.0);
        team.RatingDeviation.Should().Be(350.0);
        team.RatingVolatility.Should().Be(0.06);
        team.MatchesPlayed.Should().Be(0);
        team.Wins.Should().Be(0);
        team.Draws.Should().Be(0);
        team.Losses.Should().Be(0);
        team.GoalsFor.Should().Be(0);
        team.GoalsAgainst.Should().Be(0);
        team.Points.Should().Be(0);
        team.GoalDifference.Should().Be(0);
        team.IsRecruiting.Should().BeTrue();
        team.IsOpenForChallenges.Should().BeTrue();
        team.IsPublic.Should().BeTrue();
        team.Members.Should().ContainSingle(m => m.PlayerId == captainId && m.Role == TeamRole.Captain && m.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void CreateTeam_WithEmptyOrWhitespaceName_ShouldFail(string invalidName)
    {
        // Act
        var result = Team.Create(
            Guid.NewGuid(),
            name: invalidName,
            slug: "invalid-slug",
            city: "Cairo",
            governorate: "Cairo",
            captainId: Guid.NewGuid(),
            inviteCode: "INV001",
            nowUtc: _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.InvalidName);
    }

    [Theory]
    [InlineData("", "Cairo")]
    [InlineData("Cairo", "")]
    [InlineData("  ", "  ")]
    public void CreateTeam_WithEmptyLocation_ShouldFail(string city, string governorate)
    {
        // Act
        var result = Team.Create(
            Guid.NewGuid(),
            name: "Cairo Stars",
            slug: "cairo-stars",
            city: city,
            governorate: governorate,
            captainId: Guid.NewGuid(),
            inviteCode: "INV002",
            nowUtc: _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.InvalidLocation);
    }

    [Fact]
    public void CreateTeam_WithEmptyCaptainId_ShouldFail()
    {
        // Act
        var result = Team.Create(
            Guid.NewGuid(),
            name: "Zamalek Youth",
            slug: "zamalek-youth",
            city: "Giza",
            governorate: "Giza",
            captainId: Guid.Empty,
            inviteCode: "INV003",
            nowUtc: _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.NotCaptain);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("")]
    public void CreateTeam_WithInvalidInviteCodeLength_ShouldFail(string invalidCode)
    {
        // Act
        var result = Team.Create(
            Guid.NewGuid(),
            name: "Ahly Youth",
            slug: "ahly-youth",
            city: "Cairo",
            governorate: "Cairo",
            captainId: Guid.NewGuid(),
            inviteCode: invalidCode,
            nowUtc: _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.InvalidInviteCode);
    }

    [Fact]
    public void LeaveTeam_WhenLastCaptainAttemptsToLeave_ShouldBeRejected()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Nasr City FC",
            slug: "nasr-city-fc",
            city: "Cairo",
            governorate: "Cairo",
            captainId: captainId,
            inviteCode: "NASR01",
            nowUtc: _clock.UtcNow).Value;

        // Act: Captain attempts self-removal without transferring captaincy
        var result = team.LeaveTeam(captainId, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.SelfRemovalMustTransferCaptaincy);
        team.Members.Should().ContainSingle(m => m.PlayerId == captainId && m.IsActive);
    }

    [Fact]
    public void TransferCaptaincy_ToActiveMember_ShouldSucceedAndDemoteOldCaptain()
    {
        // Arrange
        var oldCaptainId = Guid.NewGuid();
        var newCaptainId = Guid.NewGuid();
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Maadi Wolves",
            slug: "maadi-wolves",
            city: "Cairo",
            governorate: "Cairo",
            captainId: oldCaptainId,
            inviteCode: "MAAD01",
            nowUtc: _clock.UtcNow).Value;

        team.JoinWithInviteCode("MAAD01", newCaptainId, _clock.UtcNow);

        // Act
        var result = team.TransferCaptaincy(newCaptainId, oldCaptainId, _clock.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        team.CaptainId.Should().Be(newCaptainId);

        var oldCapMember = team.Members.First(m => m.PlayerId == oldCaptainId);
        oldCapMember.Role.Should().Be(TeamRole.Member);

        var newCapMember = team.Members.First(m => m.PlayerId == newCaptainId);
        newCapMember.Role.Should().Be(TeamRole.Captain);
    }

    [Fact]
    public void TransferCaptaincy_ByNonCaptain_ShouldBeRejected()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var otherMemberId = Guid.NewGuid();

        var team = Team.Create(
            Guid.NewGuid(),
            name: "Heliopolis Kings",
            slug: "heliopolis-kings",
            city: "Cairo",
            governorate: "Cairo",
            captainId: captainId,
            inviteCode: "HELI01",
            nowUtc: _clock.UtcNow).Value;

        team.JoinWithInviteCode("HELI01", memberId, _clock.UtcNow);
        team.JoinWithInviteCode("HELI01", otherMemberId, _clock.UtcNow);

        // Act: Non-captain tries to transfer captaincy
        var result = team.TransferCaptaincy(otherMemberId, currentCaptainPlayerId: memberId, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.NotCaptain);
        team.CaptainId.Should().Be(captainId);
    }

    [Fact]
    public void TransferCaptaincy_ToSelf_ShouldBeRejected()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Shubra Tigers",
            slug: "shubra-tigers",
            city: "Cairo",
            governorate: "Cairo",
            captainId: captainId,
            inviteCode: "SHUB01",
            nowUtc: _clock.UtcNow).Value;

        // Act
        var result = team.TransferCaptaincy(captainId, captainId, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.CannotTransferToSelf);
    }

    [Fact]
    public void TransferCaptaincy_ToNonMember_ShouldBeRejected()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var nonMemberId = Guid.NewGuid();
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Giza Lions",
            slug: "giza-lions",
            city: "Giza",
            governorate: "Giza",
            captainId: captainId,
            inviteCode: "GIZA01",
            nowUtc: _clock.UtcNow).Value;

        // Act
        var result = team.TransferCaptaincy(nonMemberId, captainId, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.MemberNotFound);
    }

    [Fact]
    public void RemoveMember_CaptainRemovesMember_ShouldSucceed()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Alexandria Waves",
            slug: "alex-waves",
            city: "Alexandria",
            governorate: "Alexandria",
            captainId: captainId,
            inviteCode: "ALEX01",
            nowUtc: _clock.UtcNow).Value;

        team.JoinWithInviteCode("ALEX01", memberId, _clock.UtcNow);

        // Act
        var result = team.RemoveMember(memberId, requestedByPlayerId: captainId, _clock.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        team.Members.First(m => m.PlayerId == memberId).IsActive.Should().BeFalse();
    }

    [Fact]
    public void RemoveMember_RegularMemberRemovesCaptain_ShouldBeRejected()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Mansoura Titans",
            slug: "mansoura-titans",
            city: "Mansoura",
            governorate: "Dakahlia",
            captainId: captainId,
            inviteCode: "MANS01",
            nowUtc: _clock.UtcNow).Value;

        team.JoinWithInviteCode("MANS01", memberId, _clock.UtcNow);

        // Act: Regular member attempts to remove captain
        var result = team.RemoveMember(captainId, requestedByPlayerId: memberId, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.CannotRemoveCaptain);
    }

    [Fact]
    public void RemoveMember_RegularMemberRemovesAnotherMember_ShouldBeRejected()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();

        var team = Team.Create(
            Guid.NewGuid(),
            name: "Tanta Warriors",
            slug: "tanta-warriors",
            city: "Tanta",
            governorate: "Gharbia",
            captainId: captainId,
            inviteCode: "TANT01",
            nowUtc: _clock.UtcNow).Value;

        team.JoinWithInviteCode("TANT01", memberA, _clock.UtcNow);
        team.JoinWithInviteCode("TANT01", memberB, _clock.UtcNow);

        // Act: memberA attempts to remove memberB
        var result = team.RemoveMember(memberB, requestedByPlayerId: memberA, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.NotAuthorizedToManageRoster);
    }

    [Fact]
    public void TeamRosterCap_WhenRosterReachesMaxMembers25_ShouldRejectAdditionalJoins()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Suez Oilers",
            slug: "suez-oilers",
            city: "Suez",
            governorate: "Suez",
            captainId: captainId,
            inviteCode: "SUEZ01",
            nowUtc: _clock.UtcNow).Value;

        // Add 24 members to reach maximum 25 (including captain)
        for (var i = 0; i < 24; i++)
        {
            var joinResult = team.JoinWithInviteCode("SUEZ01", Guid.NewGuid(), _clock.UtcNow);
            joinResult.IsSuccess.Should().BeTrue();
        }

        team.Members.Count(m => m.IsActive).Should().Be(Team.MaxTeamMembers);

        // Act: Attempt to join as 26th member
        var overflowPlayerId = Guid.NewGuid();
        var overflowResult = team.JoinWithInviteCode("SUEZ01", overflowPlayerId, _clock.UtcNow);

        // Assert
        overflowResult.IsFailure.Should().BeTrue();
        overflowResult.Error.Should().Be(TeamErrors.TeamRosterFull);
    }

    [Fact]
    public void JoinWithInviteCode_WhenCodeIsExpired_ShouldBeRejected()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var expiresAt = _clock.UtcNow.AddDays(1);
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Ismailia Eagles",
            slug: "ismailia-eagles",
            city: "Ismailia",
            governorate: "Ismailia",
            captainId: captainId,
            inviteCode: "ISMA01",
            nowUtc: _clock.UtcNow,
            inviteCodeExpiresAtUtc: expiresAt).Value;

        // Advance past expiration
        _clock.Advance(TimeSpan.FromDays(2));

        // Act
        var result = team.JoinWithInviteCode("ISMA01", Guid.NewGuid(), _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.InviteCodeExpired);
    }

    [Fact]
    public void CreateJoinRequest_WhenPlayerAlreadyIn5Teams_ShouldBeRejected()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var candidatePlayerId = Guid.NewGuid();
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Aswan Hawks",
            slug: "aswan-hawks",
            city: "Aswan",
            governorate: "Aswan",
            captainId: captainId,
            inviteCode: "ASWN01",
            nowUtc: _clock.UtcNow).Value;

        // Act: Player has activeTeamsCount = 5
        var result = team.CreateJoinRequest(candidatePlayerId, "I want to join", activeTeamsCount: 5, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.MaxPlayerTeamsExceeded);
    }

    [Fact]
    public void CreateJoinRequest_WhenTeamNotRecruiting_ShouldBeRejected()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var candidatePlayerId = Guid.NewGuid();
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Fayoum Crocs",
            slug: "fayoum-crocs",
            city: "Fayoum",
            governorate: "Fayoum",
            captainId: captainId,
            inviteCode: "FAYM01",
            nowUtc: _clock.UtcNow).Value;

        team.UpdateSettings(isPublic: true, isRecruiting: false, isOpenForChallenges: true, captainId, _clock.UtcNow);

        // Act
        var result = team.CreateJoinRequest(candidatePlayerId, "Hello", activeTeamsCount: 1, _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TeamErrors.TeamNotRecruiting);
    }

    [Fact]
    public void TeamSuspension_LifecycleAndMembershipGuards_ShouldOperateCorrectly()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var team = Team.Create(
            Guid.NewGuid(),
            name: "Port Said Sailors",
            slug: "portsaid-sailors",
            city: "Port Said",
            governorate: "Port Said",
            captainId: captainId,
            inviteCode: "PORT01",
            nowUtc: _clock.UtcNow).Value;

        // 1. Suspend team with valid reason
        var suspendResult = team.Suspend("Repeated fair play violations in match #402", _clock.UtcNow);
        suspendResult.IsSuccess.Should().BeTrue();
        team.IsSuspended.Should().BeTrue();
        team.IsRecruiting.Should().BeFalse();
        team.IsOpenForChallenges.Should().BeFalse();

        // 2. Suspending again should fail
        var doubleSuspend = team.Suspend("Another reason", _clock.UtcNow);
        doubleSuspend.IsFailure.Should().BeTrue();
        doubleSuspend.Error.Code.Should().Be("Team.AlreadySuspended");

        // 3. New joins rejected while suspended
        var joinResult = team.JoinWithInviteCode("PORT01", candidateId, _clock.UtcNow);
        joinResult.IsFailure.Should().BeTrue();
        joinResult.Error.Should().Be(TeamErrors.Suspended);

        var requestResult = team.CreateJoinRequest(candidateId, "Join please", activeTeamsCount: 1, _clock.UtcNow);
        requestResult.IsFailure.Should().BeTrue();
        requestResult.Error.Should().Be(TeamErrors.Suspended);

        // 4. Reactivation restores standing
        var reactivateResult = team.Reactivate(_clock.UtcNow);
        reactivateResult.IsSuccess.Should().BeTrue();
        team.IsSuspended.Should().BeFalse();
    }

    [Fact]
    public async Task CreateTeamHandler_WhenCaptainAlreadyHas5ActiveTeams_ShouldBeRejectedAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var clock = new FakeClock(_clock.UtcNow);
        var captainId = Guid.NewGuid();

        // Pre-populate 5 teams where captainId is already an active member/captain
        for (var i = 1; i <= 5; i++)
        {
            var t = Team.Create(
                Guid.NewGuid(),
                name: $"Existing Team {i}",
                slug: $"existing-team-{i}",
                city: "Cairo",
                governorate: "Cairo",
                captainId: captainId,
                inviteCode: $"EX000{i}",
                nowUtc: clock.UtcNow).Value;
            db.Teams.Add(t);
        }
        await db.SaveChangesAsync();

        var handler = new CreateTeamHandler(
            db,
            clock,
            new TeamSlugGenerator(),
            new TeamInviteCodeGenerator());

        var command = new CreateTeamCommand(
            captainId,
            Name: "Sixth Team Attempt",
            Description: null,
            City: "Cairo",
            Governorate: "Cairo");

        // Act: Attempt to create 6th team
        var result = await handler.HandleAsync(command);

        // Assert: Expected rejection per Team.MaxTeamsPerPlayer = 5
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(TeamErrors.MaxPlayerTeamsExceeded.Code);
    }

    [Fact]
    public async Task AcceptTeamInvitationHandler_WhenPlayerAlreadyIn5Teams_ShouldBeRejectedAsync()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var clock = new FakeClock(_clock.UtcNow);
        var playerId = Guid.NewGuid();

        // Pre-populate 5 teams where playerId is an active member
        for (var i = 1; i <= 5; i++)
        {
            var t = Team.Create(
                Guid.NewGuid(),
                name: $"Team {i}",
                slug: $"team-{i}",
                city: "Cairo",
                governorate: "Cairo",
                captainId: Guid.NewGuid(),
                inviteCode: $"TM000{i}",
                nowUtc: clock.UtcNow).Value;
            t.JoinWithInviteCode($"TM000{i}", playerId, clock.UtcNow);
            db.Teams.Add(t);
        }

        // Create target team with invite code
        var targetTeam = Team.Create(
            Guid.NewGuid(),
            name: "Target Team",
            slug: "target-team",
            city: "Cairo",
            governorate: "Cairo",
            captainId: Guid.NewGuid(),
            inviteCode: "TARGET",
            nowUtc: clock.UtcNow).Value;
        db.Teams.Add(targetTeam);
        await db.SaveChangesAsync();

        var handler = new AcceptTeamInvitationHandler(db, clock);
        var command = new AcceptTeamInvitationCommand(InviteCode: "TARGET", PlayerId: playerId);

        // Act: Attempt to join 6th team via invite code
        var result = await handler.HandleAsync(command);

        // Assert: Expected rejection per Team.MaxTeamsPerPlayer = 5
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(TeamErrors.MaxPlayerTeamsExceeded.Code);
    }
}
