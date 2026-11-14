using FluentAssertions;
using Malabak.Modules.Teams.Application.Teams.CreateTeam;
using Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.ApproveJoinRequest;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.DeclineJoinRequest;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.GetTeamJoinRequests;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.RequestToJoinTeam;
using Malabak.Modules.Teams.Application.Teams.Members.LeaveTeam;
using Malabak.Modules.Teams.Application.Teams.Members.RemoveTeamMember;
using Malabak.Modules.Teams.Application.Teams.Members.TransferCaptaincy;
using Malabak.Modules.Teams.Application.Teams.Queries.DiscoverTeams;
using Malabak.Modules.Teams.Application.Teams.Queries.GetMyTeams;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamById;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamProfile;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamStats;
using Malabak.Modules.Teams.Application.Teams.Settings.UpdateTeamSettings;
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

    [Fact]
    public async Task DiscoverTeams_ShouldFilterBySearchCityAndRating_WithPaginationAsync()
    {
        await using var db = CreateInMemoryDb();
        var handler = new DiscoverTeamsHandler(db);

        var captain1 = Guid.NewGuid();
        var captain2 = Guid.NewGuid();
        var captain3 = Guid.NewGuid();

        var team1 = Team.Create(Guid.NewGuid(), "Cairo Falcons", "cairo-falcons", "Cairo", "Cairo", captain1, "CFC001", _clock.UtcNow).Value;
        team1.UpdateRanking(1600.0, 300.0, 0.06, _clock.UtcNow);

        var team2 = Team.Create(Guid.NewGuid(), "Cairo Eagles", "cairo-eagles", "Cairo", "Cairo", captain2, "CEC002", _clock.UtcNow).Value;
        team2.UpdateRanking(1400.0, 300.0, 0.06, _clock.UtcNow);

        var team3 = Team.Create(Guid.NewGuid(), "Alexandria Sharks", "alex-sharks", "Alexandria", "Alexandria", captain3, "ASH003", _clock.UtcNow).Value;
        team3.UpdateRanking(1700.0, 250.0, 0.06, _clock.UtcNow);

        db.Teams.AddRange(team1, team2, team3);
        await db.SaveChangesAsync();

        // 1. Search text filter
        var searchRes = await handler.HandleAsync(new DiscoverTeamsQuery(Search: "Eagles"));
        searchRes.IsSuccess.Should().BeTrue();
        searchRes.Value.TotalCount.Should().Be(1);
        searchRes.Value.Items[0].Name.Should().Be("Cairo Eagles");

        // 2. City filter
        var cityRes = await handler.HandleAsync(new DiscoverTeamsQuery(City: "Cairo"));
        cityRes.IsSuccess.Should().BeTrue();
        cityRes.Value.TotalCount.Should().Be(2);

        // 3. Rating range filter
        var ratingRes = await handler.HandleAsync(new DiscoverTeamsQuery(MinRating: 1500.0));
        ratingRes.IsSuccess.Should().BeTrue();
        ratingRes.Value.TotalCount.Should().Be(2);
        ratingRes.Value.Items.Should().Contain(t => t.Name == "Alexandria Sharks");
        ratingRes.Value.Items.Should().Contain(t => t.Name == "Cairo Falcons");

        // 4. Pagination
        var pagedRes = await handler.HandleAsync(new DiscoverTeamsQuery(Page: 1, PageSize: 2));
        pagedRes.IsSuccess.Should().BeTrue();
        pagedRes.Value.Items.Should().HaveCount(2);
        pagedRes.Value.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task DiscoverTeams_ShouldProtectPrivateTeams_FromDiscoveryAsync()
    {
        await using var db = CreateInMemoryDb();
        var handler = new DiscoverTeamsHandler(db);

        var cap1 = Guid.NewGuid();
        var cap2 = Guid.NewGuid();

        // Public team
        var publicTeam = Team.Create(Guid.NewGuid(), "Public Lions", "public-lions", "Cairo", "Cairo", cap1, "PUB001", _clock.UtcNow, isPublic: true).Value;

        // Private team
        var privateTeam = Team.Create(Guid.NewGuid(), "Secret Agents", "secret-agents", "Cairo", "Cairo", cap2, "SEC002", _clock.UtcNow, isPublic: false).Value;

        db.Teams.AddRange(publicTeam, privateTeam);
        await db.SaveChangesAsync();

        // Act - Discover all
        var result = await handler.HandleAsync(new DiscoverTeamsQuery());

        // Assert - Private team must NOT be returned in discovery
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle(t => t.Id == publicTeam.Id);
        result.Value.Items.Should().NotContain(t => t.Id == privateTeam.Id);
    }

    [Fact]
    public async Task DiscoverTeams_ShouldCorrectlySetChallengeEligibilityFlagAsync()
    {
        await using var db = CreateInMemoryDb();
        var handler = new DiscoverTeamsHandler(db);

        var myPlayerId = Guid.NewGuid();
        var opponentCaptainId = Guid.NewGuid();

        // 1. Team open for challenges where caller is not a member
        var openTeam = Team.Create(Guid.NewGuid(), "Open FC", "open-fc", "Cairo", "Cairo", opponentCaptainId, "OPN001", _clock.UtcNow).Value;

        // 2. Team closed for challenges
        var closedTeam = Team.Create(Guid.NewGuid(), "Closed FC", "closed-fc", "Cairo", "Cairo", opponentCaptainId, "CLS002", _clock.UtcNow).Value;
        closedTeam.UpdateProfile("Closed FC", null, "Cairo", "Cairo", null, null, null, isRecruiting: false, isOpenForChallenges: false, opponentCaptainId, _clock.UtcNow);

        // 3. Team where caller is Captain/Member
        var myTeam = Team.Create(Guid.NewGuid(), "My Squad", "my-squad", "Cairo", "Cairo", myPlayerId, "MYS003", _clock.UtcNow).Value;

        db.Teams.AddRange(openTeam, closedTeam, myTeam);
        await db.SaveChangesAsync();

        // Act - Discover with RequestingPlayerId
        var result = await handler.HandleAsync(new DiscoverTeamsQuery(RequestingPlayerId: myPlayerId));

        // Assert
        result.IsSuccess.Should().BeTrue();

        var openDto = result.Value.Items.First(t => t.Id == openTeam.Id);
        openDto.CanBeChallenged.Should().BeTrue();

        var closedDto = result.Value.Items.First(t => t.Id == closedTeam.Id);
        closedDto.CanBeChallenged.Should().BeFalse();

        var myTeamDto = result.Value.Items.First(t => t.Id == myTeam.Id);
        myTeamDto.CanBeChallenged.Should().BeFalse("player cannot challenge their own team");
    }

    [Fact]
    public async Task GetTeamProfile_ShouldReturnHeaderRosterAndStatsProjectionAsync()
    {
        await using var db = CreateInMemoryDb();
        var handler = new GetTeamProfileHandler(db);

        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var team = Team.Create(Guid.NewGuid(), "Champions FC", "champions-fc", "Cairo", "Cairo", captainId, "CMP001", _clock.UtcNow).Value;
        team.JoinWithInviteCode("CMP001", memberId, _clock.UtcNow);
        team.RecordMatchResult(isWin: true, isDraw: false, isLoss: false, goalsFor: 4, goalsAgainst: 1, _clock.UtcNow);
        team.RecordMatchResult(isWin: false, isDraw: true, isLoss: false, goalsFor: 2, goalsAgainst: 2, _clock.UtcNow);
        team.UpdateRanking(1650.0, 200.0, 0.05, _clock.UtcNow);

        db.Teams.Add(team);
        await db.SaveChangesAsync();

        // Act - Member views profile
        var profileRes = await handler.HandleAsync(new GetTeamProfileQuery(team.Id, memberId));

        // Assert
        profileRes.IsSuccess.Should().BeTrue();
        var profile = profileRes.Value;
        profile.Name.Should().Be("Champions FC");
        profile.IsCallerMember.Should().BeTrue();
        profile.CallerRole.Should().Be(TeamRole.Member);
        profile.InviteCode.Should().Be("CMP001");
        profile.Roster.Should().HaveCount(2);

        // Stats projection
        profile.Stats.MatchesPlayed.Should().Be(2);
        profile.Stats.Wins.Should().Be(1);
        profile.Stats.Draws.Should().Be(1);
        profile.Stats.Losses.Should().Be(0);
        profile.Stats.GoalsFor.Should().Be(6);
        profile.Stats.GoalsAgainst.Should().Be(3);
        profile.Stats.GoalDifference.Should().Be(3);
        profile.Stats.WinPercentage.Should().Be(50.0);
        profile.Stats.Rating.Should().Be(1650.0);
        profile.Stats.Rank.Should().Be(1);
    }

    [Fact]
    public async Task GetTeamProfile_RosterIncludesPlayerNamesAsync()
    {
        await using var db = CreateInMemoryDb();
        var captainId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Names FC", "names-fc", "Cairo", "Cairo", captainId, "NMS001", _clock.UtcNow).Value;
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var profile = await new GetTeamProfileHandler(db, new NamedAccounts()).HandleAsync(new GetTeamProfileQuery(team.Id, captainId));

        profile.Value.Roster.Should().ContainSingle(m => m.PlayerId == captainId && m.PlayerName == "Player");
    }

    [Fact]
    public async Task GetTeamProfile_PrivateTeam_ShouldBeHiddenFromNonMembersAsync()
    {
        await using var db = CreateInMemoryDb();
        var handler = new GetTeamProfileHandler(db);

        var captainId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();

        var privateTeam = Team.Create(Guid.NewGuid(), "Stealth FC", "stealth-fc", "Cairo", "Cairo", captainId, "STL001", _clock.UtcNow, isPublic: false).Value;
        db.Teams.Add(privateTeam);
        await db.SaveChangesAsync();

        // Act 1: Stranger views private team
        var strangerRes = await handler.HandleAsync(new GetTeamProfileQuery(privateTeam.Id, strangerId));
        strangerRes.IsSuccess.Should().BeFalse();
        strangerRes.Error.Should().Be(TeamErrors.NotFound);

        // Act 2: Captain views private team
        var captainRes = await handler.HandleAsync(new GetTeamProfileQuery(privateTeam.Id, captainId));
        captainRes.IsSuccess.Should().BeTrue();
        captainRes.Value.Name.Should().Be("Stealth FC");
    }

    [Fact]
    public async Task GetTeamStats_ShouldCalculateWinPercentageGoalDiffAndRankAsync()
    {
        await using var db = CreateInMemoryDb();
        var handler = new GetTeamStatsHandler(db);

        var cap1 = Guid.NewGuid();
        var cap2 = Guid.NewGuid();

        var team1 = Team.Create(Guid.NewGuid(), "Top Dogs", "top-dogs", "Cairo", "Cairo", cap1, "TOP001", _clock.UtcNow).Value;
        team1.UpdateRanking(1800.0, 150.0, 0.05, _clock.UtcNow);
        team1.RecordMatchResult(isWin: true, isDraw: false, isLoss: false, goalsFor: 3, goalsAgainst: 0, _clock.UtcNow);
        team1.RecordMatchResult(isWin: true, isDraw: false, isLoss: false, goalsFor: 2, goalsAgainst: 1, _clock.UtcNow);
        team1.RecordMatchResult(isWin: false, isDraw: false, isLoss: true, goalsFor: 0, goalsAgainst: 1, _clock.UtcNow);

        var team2 = Team.Create(Guid.NewGuid(), "Challengers", "challengers", "Cairo", "Cairo", cap2, "CHL002", _clock.UtcNow).Value;
        team2.UpdateRanking(1900.0, 150.0, 0.05, _clock.UtcNow);

        db.Teams.AddRange(team1, team2);
        await db.SaveChangesAsync();

        // Act
        var statsRes = await handler.HandleAsync(new GetTeamStatsQuery(team1.Id));

        // Assert
        statsRes.IsSuccess.Should().BeTrue();
        var stats = statsRes.Value;
        stats.MatchesPlayed.Should().Be(3);
        stats.Wins.Should().Be(2);
        stats.Losses.Should().Be(1);
        stats.GoalsFor.Should().Be(5);
        stats.GoalsAgainst.Should().Be(2);
        stats.GoalDifference.Should().Be(3);
        stats.WinPercentage.Should().Be(66.7);
        stats.Rating.Should().Be(1800.0);
        stats.Rank.Should().Be(2, "team2 has rating 1900 so team1 is rank 2");
    }

    [Fact]
    public void Team_CreateJoinRequest_WhenNotRecruiting_ShouldFail()
    {
        var captainId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Recruiting Closed", "rec-closed", "Cairo", "Cairo", captainId, "CLS001", _clock.UtcNow).Value;
        team.UpdateSettings(isPublic: true, isRecruiting: false, isOpenForChallenges: true, captainId, _clock.UtcNow);

        var applicantId = Guid.NewGuid();
        var result = team.CreateJoinRequest(applicantId, "Let me in", activeTeamsCount: 1, _clock.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TeamErrors.TeamNotRecruiting);
    }

    [Fact]
    public void Team_CreateJoinRequest_WhenExceedsMaxTeamsPerPlayer_ShouldFail()
    {
        var captainId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Team Alpha", "team-alpha", "Cairo", "Cairo", captainId, "ALP001", _clock.UtcNow).Value;

        var applicantId = Guid.NewGuid();
        var result = team.CreateJoinRequest(applicantId, "Hi", activeTeamsCount: 5, _clock.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TeamErrors.MaxPlayerTeamsExceeded);
    }

    [Fact]
    public void Team_CreateJoinRequest_WhenRosterFull_ShouldFail()
    {
        var captainId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Full Squad", "full-squad", "Cairo", "Cairo", captainId, "FUL001", _clock.UtcNow).Value;

        // Fill roster to 25
        for (var i = 1; i < Team.MaxTeamMembers; i++)
        {
            team.JoinWithInviteCode("FUL001", Guid.NewGuid(), _clock.UtcNow);
        }

        team.Members.Count(m => m.IsActive).Should().Be(25);

        var applicantId = Guid.NewGuid();
        var result = team.CreateJoinRequest(applicantId, "Want to join", activeTeamsCount: 0, _clock.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TeamErrors.TeamRosterFull);

        var inviteJoin = team.JoinWithInviteCode("FUL001", applicantId, _clock.UtcNow);
        inviteJoin.IsSuccess.Should().BeFalse();
        inviteJoin.Error.Should().Be(TeamErrors.TeamRosterFull);
    }

    [Fact]
    public void Team_CreateJoinRequest_DuplicatePending_ShouldFail()
    {
        var captainId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Squad", "squad-1", "Cairo", "Cairo", captainId, "SQD001", _clock.UtcNow).Value;
        var applicantId = Guid.NewGuid();

        var first = team.CreateJoinRequest(applicantId, "First message", activeTeamsCount: 1, _clock.UtcNow);
        first.IsSuccess.Should().BeTrue();

        var second = team.CreateJoinRequest(applicantId, "Second message", activeTeamsCount: 1, _clock.UtcNow);
        second.IsSuccess.Should().BeFalse();
        second.Error.Should().Be(TeamErrors.DuplicateJoinRequest);
    }

    [Fact]
    public void Team_ApproveAndDeclineJoinRequest_Flow_ShouldWorkProperly()
    {
        var captainId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Squad", "squad-2", "Cairo", "Cairo", captainId, "SQD002", _clock.UtcNow).Value;

        var applicant1 = Guid.NewGuid();
        var applicant2 = Guid.NewGuid();
        var req1 = team.CreateJoinRequest(applicant1, "Hello", activeTeamsCount: 1, _clock.UtcNow).Value;
        var req2 = team.CreateJoinRequest(applicant2, "Hey", activeTeamsCount: 1, _clock.UtcNow).Value;

        // Non captain cannot approve
        var unauthorized = team.ApproveJoinRequest(req1.Id, Guid.NewGuid(), _clock.UtcNow);
        unauthorized.IsSuccess.Should().BeFalse();
        unauthorized.Error.Should().Be(TeamErrors.NotCaptainOrViceCaptain);

        // Captain approves req1
        var approveRes = team.ApproveJoinRequest(req1.Id, captainId, _clock.UtcNow);
        approveRes.IsSuccess.Should().BeTrue();
        req1.Status.Should().Be(JoinRequestStatus.Approved);
        team.Members.Should().Contain(m => m.PlayerId == applicant1 && m.IsActive);

        // Cannot re-approve
        var reapprove = team.ApproveJoinRequest(req1.Id, captainId, _clock.UtcNow);
        reapprove.IsSuccess.Should().BeFalse();
        reapprove.Error.Should().Be(TeamErrors.JoinRequestAlreadyHandled);

        // Captain declines req2
        var declineRes = team.DeclineJoinRequest(req2.Id, captainId, _clock.UtcNow);
        declineRes.IsSuccess.Should().BeTrue();
        req2.Status.Should().Be(JoinRequestStatus.Declined);
    }

    [Fact]
    public void Team_LeaveTeam_MemberCanLeave_CaptainCannotWithoutTransfer()
    {
        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Leave Squad", "leave-squad", "Cairo", "Cairo", captainId, "LVE001", _clock.UtcNow).Value;
        team.JoinWithInviteCode("LVE001", memberId, _clock.UtcNow);

        // Captain attempts to leave while another member is still there
        var captainLeave = team.LeaveTeam(captainId, _clock.UtcNow);
        captainLeave.IsSuccess.Should().BeFalse();
        captainLeave.Error.Should().Be(TeamErrors.SelfRemovalMustTransferCaptaincy);

        // Member leaves
        var memberLeave = team.LeaveTeam(memberId, _clock.UtcNow);
        memberLeave.IsSuccess.Should().BeTrue();
        team.Members.First(m => m.PlayerId == memberId).IsActive.Should().BeFalse();
    }

    [Fact]
    public void Team_UpdateSettings_OnlyCaptainOrViceCaptainAllowed()
    {
        var captainId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Settings Team", "settings-team", "Cairo", "Cairo", captainId, "SET001", _clock.UtcNow).Value;
        team.JoinWithInviteCode("SET001", memberId, _clock.UtcNow);

        var memberUpdate = team.UpdateSettings(isPublic: false, isRecruiting: false, isOpenForChallenges: false, memberId, _clock.UtcNow);
        memberUpdate.IsSuccess.Should().BeFalse();
        memberUpdate.Error.Should().Be(TeamErrors.NotCaptainOrViceCaptain);

        var captainUpdate = team.UpdateSettings(isPublic: false, isRecruiting: false, isOpenForChallenges: false, captainId, _clock.UtcNow);
        captainUpdate.IsSuccess.Should().BeTrue();
        team.IsPublic.Should().BeFalse();
        team.IsRecruiting.Should().BeFalse();
        team.IsOpenForChallenges.Should().BeFalse();
    }

    [Fact]
    public async Task JoinRequest_Handlers_FullIntegrationAsync()
    {
        await using var db = CreateInMemoryDb();
        var reqHandler = new RequestToJoinTeamHandler(db, _clock);
        var getReqHandler = new GetTeamJoinRequestsHandler(db);
        var approveHandler = new ApproveJoinRequestHandler(db, _clock);
        var declineHandler = new DeclineJoinRequestHandler(db, _clock);
        var leaveHandler = new LeaveTeamHandler(db, _clock);
        var settingsHandler = new UpdateTeamSettingsHandler(db, _clock);

        var captainId = Guid.NewGuid();
        var applicantId = Guid.NewGuid();
        var team = Team.Create(Guid.NewGuid(), "Integration FC", "integration-fc", "Cairo", "Cairo", captainId, "INT001", _clock.UtcNow).Value;
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        // 1. Submit join request
        var reqRes = await reqHandler.HandleAsync(new RequestToJoinTeamCommand(team.Id, applicantId, "Please accept"));
        reqRes.IsSuccess.Should().BeTrue();
        var requestId = reqRes.Value.Id;

        // 2. Query join requests by Captain
        var listRes = await getReqHandler.HandleAsync(new GetTeamJoinRequestsQuery(team.Id, captainId));
        listRes.IsSuccess.Should().BeTrue();
        listRes.Value.Should().ContainSingle(r => r.Id == requestId && r.Status == JoinRequestStatus.Pending);

        // Query by non-captain fails
        var listForbidden = await getReqHandler.HandleAsync(new GetTeamJoinRequestsQuery(team.Id, applicantId));
        listForbidden.IsSuccess.Should().BeFalse();
        listForbidden.Error.Should().Be(TeamErrors.NotCaptainOrViceCaptain);

        // 3. Approve join request
        var approveRes = await approveHandler.HandleAsync(new ApproveJoinRequestCommand(team.Id, requestId, captainId));
        approveRes.IsSuccess.Should().BeTrue();

        // 3b. Test decline with a second applicant
        var applicant2 = Guid.NewGuid();
        var req2Res = await reqHandler.HandleAsync(new RequestToJoinTeamCommand(team.Id, applicant2, "Another request"));
        req2Res.IsSuccess.Should().BeTrue();
        var declineRes = await declineHandler.HandleAsync(new DeclineJoinRequestCommand(team.Id, req2Res.Value.Id, captainId));
        declineRes.IsSuccess.Should().BeTrue();

        // 4. Update settings
        var settingsRes = await settingsHandler.HandleAsync(new UpdateTeamSettingsCommand(team.Id, captainId, IsPublic: true, IsRecruiting: false, IsOpenForChallenges: true));
        settingsRes.IsSuccess.Should().BeTrue();

        // 5. Leave team
        var leaveRes = await leaveHandler.HandleAsync(new LeaveTeamCommand(team.Id, applicantId));
        leaveRes.IsSuccess.Should().BeTrue();
    }

    private sealed class NamedAccounts : Malabak.SharedKernel.Contracts.IUserAccountsContract
    {
        public string? NormalizePhone(string phoneNumber) => phoneNumber;

        public Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, _ => "Player"));

        public Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
