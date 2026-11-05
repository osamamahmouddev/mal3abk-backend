using FluentAssertions;
using Malabak.Modules.Teams.Application.Teams.CreateTeam;
using Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Members.RemoveTeamMember;
using Malabak.Modules.Teams.Application.Teams.Members.TransferCaptaincy;
using Malabak.Modules.Teams.Application.Teams.Queries.GetMyTeams;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamById;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Domain.Events;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Teams.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class TeamTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc));
    private readonly TeamSlugGenerator _slugGenerator = new();
    private readonly TeamInviteCodeGenerator _inviteCodeGenerator = new();

    private static TeamsDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<TeamsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TeamsDbContext(options);
    }

    [Fact]
    public void Team_Create_ShouldAssignCreatorAsCaptain_AndRaiseEvents()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var captainId = Guid.NewGuid();
        var now = _clock.UtcNow;

        // Act
        var result = Team.Create(
            teamId,
            "Cairo Knights",
            "cairo-knights-1234",
            "Cairo",
            "Cairo",
            captainId,
            "ABC123",
            now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var team = result.Value;
        team.Id.Should().Be(teamId);
        team.Name.Should().Be("Cairo Knights");
        team.Slug.Should().Be("cairo-knights-1234");
        team.CaptainId.Should().Be(captainId);
        team.InviteCode.Should().Be("ABC123");
        team.Members.Should().HaveCount(1);

        var captain = team.Members.First();
        captain.PlayerId.Should().Be(captainId);
        captain.Role.Should().Be(TeamRole.Captain);
        captain.IsActive.Should().BeTrue();

        team.DomainEvents.Should().ContainSingle(e => e is TeamCreatedDomainEvent);
        team.DomainEvents.Should().ContainSingle(e => e is TeamMemberJoinedDomainEvent);
    }

    [Fact]
    public void Team_Create_WithInvalidData_ShouldFail()
    {
        var captainId = Guid.NewGuid();
        var now = _clock.UtcNow;

        var emptyName = Team.Create(Guid.NewGuid(), "", "slug-1", "Cairo", "Cairo", captainId, "ABC123", now);
        emptyName.IsSuccess.Should().BeFalse();
        emptyName.Error.Should().Be(TeamErrors.InvalidName);

        var emptyCity = Team.Create(Guid.NewGuid(), "Knights", "slug-2", "", "Cairo", captainId, "ABC123", now);
        emptyCity.IsSuccess.Should().BeFalse();
        emptyCity.Error.Should().Be(TeamErrors.InvalidLocation);

        var invalidCode = Team.Create(Guid.NewGuid(), "Knights", "slug-3", "Cairo", "Cairo", captainId, "123", now);
        invalidCode.IsSuccess.Should().BeFalse();
        invalidCode.Error.Should().Be(TeamErrors.InvalidInviteCode);
    }

    [Fact]
    public void TeamSlugGenerator_ShouldGenerateCleanLowercaseSlugWithSuffix()
    {
        var slug = _slugGenerator.GenerateSlug("Alexandria Thunder FC");
        slug.Should().StartWith("alexandria-thunder-fc-");
        slug.Length.Should().BeGreaterThan("alexandria-thunder-fc-".Length);

        // Arabic name fallback
        var arabicSlug = _slugGenerator.GenerateSlug("فريق النسور الذهبية");
        arabicSlug.Should().StartWith("team-");
    }

    [Fact]
    public void TeamInviteCodeGenerator_ShouldGenerate6CharacterAlphanumericCode()
    {
        var code = _inviteCodeGenerator.GenerateCode();
        code.Should().HaveLength(6);
        code.Should().MatchRegex("^[A-Z0-9]{6}$");
    }

    [Fact]
    public void JoinWithInviteCode_WithValidCode_ShouldAddMember()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var newPlayerId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Giza Stars", "giza-stars", "Giza", "Giza", captainId, "GIZA99", _clock.UtcNow).Value;

        // Act
        var joinResult = team.JoinWithInviteCode("GIZA99", newPlayerId, _clock.UtcNow);

        // Assert
        joinResult.IsSuccess.Should().BeTrue();
        team.Members.Should().HaveCount(2);

        var newMember = team.Members.First(m => m.PlayerId == newPlayerId);
        newMember.Role.Should().Be(TeamRole.Member);
        newMember.IsActive.Should().BeTrue();
    }

    [Fact]
    public void JoinWithInviteCode_WithInvalidOrExpiredCode_ShouldFail()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var newPlayerId = Guid.NewGuid();
        var expiresAt = _clock.UtcNow.AddMinutes(-5); // Expired 5 mins ago
        var team = Team.Create(
            Guid.NewGuid(), "Giza Stars", "giza-stars", "Giza", "Giza", captainId, "GIZA99", _clock.UtcNow,
            inviteCodeExpiresAtUtc: expiresAt).Value;

        // Act 1: Invalid code
        var invalidResult = team.JoinWithInviteCode("WRONG1", newPlayerId, _clock.UtcNow);
        invalidResult.IsSuccess.Should().BeFalse();
        invalidResult.Error.Should().Be(TeamErrors.InvalidInviteCode);

        // Act 2: Expired code
        var expiredResult = team.JoinWithInviteCode("GIZA99", newPlayerId, _clock.UtcNow);
        expiredResult.IsSuccess.Should().BeFalse();
        expiredResult.Error.Should().Be(TeamErrors.InviteCodeExpired);
    }

    [Fact]
    public void JoinWithInviteCode_WhenAlreadyMember_ShouldFail()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var newPlayerId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Giza Stars", "giza-stars", "Giza", "Giza", captainId, "GIZA99", _clock.UtcNow).Value;
        team.JoinWithInviteCode("GIZA99", newPlayerId, _clock.UtcNow);

        // Act - Attempt to join again
        var duplicateJoin = team.JoinWithInviteCode("GIZA99", newPlayerId, _clock.UtcNow);

        // Assert
        duplicateJoin.IsSuccess.Should().BeFalse();
        duplicateJoin.Error.Should().Be(TeamErrors.PlayerAlreadyInTeam);
    }

    [Fact]
    public void Captain_CanRemoveMember_Successfully()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Delta Tigers", "delta-tigers", "Tanta", "Gharbia", captainId, "DELTA1", _clock.UtcNow).Value;
        team.JoinWithInviteCode("DELTA1", memberId, _clock.UtcNow);

        // Act - Captain removes member
        var removeResult = team.RemoveMember(memberId, captainId, _clock.UtcNow);

        // Assert
        removeResult.IsSuccess.Should().BeTrue();
        var member = team.Members.First(m => m.PlayerId == memberId);
        member.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Member_CanLeaveTeam_Successfully()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Delta Tigers", "delta-tigers", "Tanta", "Gharbia", captainId, "DELTA1", _clock.UtcNow).Value;
        team.JoinWithInviteCode("DELTA1", memberId, _clock.UtcNow);

        // Act - Member leaves themselves
        var leaveResult = team.RemoveMember(memberId, memberId, _clock.UtcNow);

        // Assert
        leaveResult.IsSuccess.Should().BeTrue();
        var member = team.Members.First(m => m.PlayerId == memberId);
        member.IsActive.Should().BeFalse();
    }

    [Fact]
    public void NonCaptain_CannotRemoveOtherMembers()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var member1Id = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Delta Tigers", "delta-tigers", "Tanta", "Gharbia", captainId, "DELTA1", _clock.UtcNow).Value;
        team.JoinWithInviteCode("DELTA1", member1Id, _clock.UtcNow);
        team.JoinWithInviteCode("DELTA1", member2Id, _clock.UtcNow);

        // Act - Member1 attempts to remove Member2
        var unauthorizedRemove = team.RemoveMember(member2Id, member1Id, _clock.UtcNow);

        // Assert
        unauthorizedRemove.IsSuccess.Should().BeFalse();
        unauthorizedRemove.Error.Should().Be(TeamErrors.NotAuthorizedToManageRoster);
    }

    [Fact]
    public void Captain_CannotBeRemoved_OrLeaveWithoutTransferringCaptaincy()
    {
        // Arrange
        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Delta Tigers", "delta-tigers", "Tanta", "Gharbia", captainId, "DELTA1", _clock.UtcNow).Value;
        team.JoinWithInviteCode("DELTA1", memberId, _clock.UtcNow);

        // Act 1: Another member tries to remove captain
        var removeCaptainResult = team.RemoveMember(captainId, memberId, _clock.UtcNow);
        removeCaptainResult.IsSuccess.Should().BeFalse();

        // Act 2: Captain tries to leave themselves without transferring
        var captainLeaveResult = team.RemoveMember(captainId, captainId, _clock.UtcNow);
        captainLeaveResult.IsSuccess.Should().BeFalse();
        captainLeaveResult.Error.Should().Be(TeamErrors.SelfRemovalMustTransferCaptaincy);
    }

    [Fact]
    public void TransferCaptaincy_ByCaptain_ToActiveMember_ShouldSucceed()
    {
        // Arrange
        var oldCaptainId = Guid.NewGuid();
        var newCaptainId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Red Sea Hawks", "red-sea-hawks", "Hurghada", "Red Sea", oldCaptainId, "RED001", _clock.UtcNow).Value;
        team.JoinWithInviteCode("RED001", newCaptainId, _clock.UtcNow);

        // Act
        var transferResult = team.TransferCaptaincy(newCaptainId, oldCaptainId, _clock.UtcNow);

        // Assert
        transferResult.IsSuccess.Should().BeTrue();
        team.CaptainId.Should().Be(newCaptainId);

        var newCapMember = team.Members.First(m => m.PlayerId == newCaptainId);
        newCapMember.Role.Should().Be(TeamRole.Captain);

        var oldCapMember = team.Members.First(m => m.PlayerId == oldCaptainId);
        oldCapMember.Role.Should().Be(TeamRole.Member);
    }

    [Fact]
    public void TransferCaptaincy_ByNonCaptain_OrToNonMember_ShouldFail()
    {
        // Arrange
        var oldCaptainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var nonMemberId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Red Sea Hawks", "red-sea-hawks", "Hurghada", "Red Sea", oldCaptainId, "RED001", _clock.UtcNow).Value;
        team.JoinWithInviteCode("RED001", memberId, _clock.UtcNow);

        // Act 1: Non-captain tries to transfer
        var nonCaptainTransfer = team.TransferCaptaincy(memberId, memberId, _clock.UtcNow);
        nonCaptainTransfer.IsSuccess.Should().BeFalse();
        nonCaptainTransfer.Error.Should().Be(TeamErrors.NotCaptain);

        // Act 2: Captain transfers to non-member
        var nonMemberTransfer = team.TransferCaptaincy(nonMemberId, oldCaptainId, _clock.UtcNow);
        nonMemberTransfer.IsSuccess.Should().BeFalse();
        nonMemberTransfer.Error.Should().Be(TeamErrors.MemberNotFound);

        // Act 3: Captain transfers to self
        var selfTransfer = team.TransferCaptaincy(oldCaptainId, oldCaptainId, _clock.UtcNow);
        selfTransfer.IsSuccess.Should().BeFalse();
        selfTransfer.Error.Should().Be(TeamErrors.CannotTransferToSelf);
    }

    [Fact]
    public async Task EndToEndHandler_CreateTeam_Invite_Accept_Remove_AndTransferAsync()
    {
        await using var db = CreateInMemoryDb();

        var createHandler = new CreateTeamHandler(db, _clock, _slugGenerator, _inviteCodeGenerator);
        var inviteHandler = new CreateTeamInvitationHandler(db, _clock, _inviteCodeGenerator);
        var acceptHandler = new AcceptTeamInvitationHandler(db, _clock);
        var removeHandler = new RemoveTeamMemberHandler(db, _clock);
        var transferHandler = new TransferCaptaincyHandler(db, _clock);
        var getByIdHandler = new GetTeamByIdHandler(db);
        var getMyTeamsHandler = new GetMyTeamsHandler(db);

        var captainId = Guid.NewGuid();
        var player2Id = Guid.NewGuid();
        var player3Id = Guid.NewGuid();

        // 1. Create Team
        var createCmd = new CreateTeamCommand(
            captainId, "Zamalek Wolves", "Best squad in town", "Cairo", "Cairo", "Zamalek");
        var createRes = await createHandler.HandleAsync(createCmd);
        createRes.IsSuccess.Should().BeTrue();
        var teamId = createRes.Value.Id;
        createRes.Value.CaptainId.Should().Be(captainId);
        createRes.Value.InviteCode.Should().NotBeNullOrWhiteSpace();

        // 2. Rotate / Create new Invite Code
        var inviteCmd = new CreateTeamInvitationCommand(teamId, captainId, ValidityDays: 14);
        var inviteRes = await inviteHandler.HandleAsync(inviteCmd);
        inviteRes.IsSuccess.Should().BeTrue();
        var newCode = inviteRes.Value.InviteCode;
        newCode.Should().HaveLength(6);

        // 3. Player 2 joins via invite code
        var acceptCmd = new AcceptTeamInvitationCommand(newCode, player2Id);
        var acceptRes = await acceptHandler.HandleAsync(acceptCmd);
        acceptRes.IsSuccess.Should().BeTrue();
        acceptRes.Value.Members.Should().HaveCount(2);

        // 4. Player 3 joins via invite code
        var accept3Cmd = new AcceptTeamInvitationCommand(newCode, player3Id);
        var accept3Res = await acceptHandler.HandleAsync(accept3Cmd);
        accept3Res.IsSuccess.Should().BeTrue();
        accept3Res.Value.Members.Should().HaveCount(3);

        // 5. Transfer Captaincy from captain to Player 2
        var transferCmd = new TransferCaptaincyCommand(teamId, player2Id, captainId);
        var transferRes = await transferHandler.HandleAsync(transferCmd);
        transferRes.IsSuccess.Should().BeTrue();

        // Verify with GetTeamById
        var teamDetail = await getByIdHandler.HandleAsync(new GetTeamByIdQuery(teamId, player2Id));
        teamDetail.IsSuccess.Should().BeTrue();
        teamDetail.Value.CaptainId.Should().Be(player2Id);

        // 6. Player 2 (new captain) removes Player 3
        var removeCmd = new RemoveTeamMemberCommand(teamId, player3Id, player2Id);
        var removeRes = await removeHandler.HandleAsync(removeCmd);
        removeRes.IsSuccess.Should().BeTrue();

        // 7. Verify MyTeams for Player 2 and Player 3
        var myTeamsPlayer2 = await getMyTeamsHandler.HandleAsync(new GetMyTeamsQuery(player2Id));
        myTeamsPlayer2.Value.Should().ContainSingle(t => t.Id == teamId);

        var myTeamsPlayer3 = await getMyTeamsHandler.HandleAsync(new GetMyTeamsQuery(player3Id));
        myTeamsPlayer3.Value.Should().BeEmpty();
    }
}
