using FluentAssertions;
using Malabak.Modules.Teams.Application.Teams.CreateTeam;
using Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.ApproveJoinRequest;
using Malabak.Modules.Teams.Application.Teams.Members.LeaveTeam;
using Malabak.Modules.Teams.Application.Teams.Members.RemoveTeamMember;
using Malabak.Modules.Teams.Application.Teams.Members.UpdateMemberRole;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Teams.Infrastructure.Services;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// The Teams logic gaps found by the app's integration suite: removed players rejoining, the 5-teams limit on approval,
/// stale join requests, lone captains, role assignment and privacy at creation.
/// </summary>
public sealed class TeamLogicGapTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc));

    private static TeamsDbContext CreateInMemoryDb() =>
        new(new DbContextOptionsBuilder<TeamsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private Team NewTeam(Guid captainId, string code = "ABC234", string slug = "team") =>
        Team.Create(Guid.NewGuid(), "Team", slug, "Cairo", "Cairo", captainId, code, _clock.UtcNow).Value;

    // ---------- 1. Removed player and the shared code ----------

    [Fact]
    public void JoinWithInviteCode_AfterRemovalByCaptain_ReturnsRemovedFromTeam()
    {
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        team.JoinWithInviteCode("ABC234", player, _clock.UtcNow).IsSuccess.Should().BeTrue();
        team.RemoveMember(player, captain, _clock.UtcNow).IsSuccess.Should().BeTrue();

        var result = team.JoinWithInviteCode("ABC234", player, _clock.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(TeamErrors.RemovedFromTeam.Code);
    }

    [Fact]
    public void JoinWithInviteCode_AfterLeavingVoluntarily_Succeeds()
    {
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        team.JoinWithInviteCode("ABC234", player, _clock.UtcNow);
        team.LeaveTeam(player, _clock.UtcNow).IsSuccess.Should().BeTrue();

        team.JoinWithInviteCode("ABC234", player, _clock.UtcNow).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void JoinWithInviteCode_WhenRejoinBlockedFlagPassed_ReturnsRemovedFromTeam()
    {
        var team = NewTeam(Guid.NewGuid());

        var result = team.JoinWithInviteCode("ABC234", Guid.NewGuid(), _clock.UtcNow, rejoinBlocked: true);

        result.Error.Code.Should().Be(TeamErrors.RemovedFromTeam.Code);
    }

    [Fact]
    public async Task AcceptHandler_RemovedPlayerWithOldCode_IsRefusedAsync()
    {
        using var db = CreateInMemoryDb();
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        (await new AcceptTeamInvitationHandler(db, _clock).HandleAsync(new AcceptTeamInvitationCommand("ABC234", player))).IsSuccess.Should().BeTrue();
        (await new RemoveTeamMemberHandler(db, _clock).HandleAsync(new RemoveTeamMemberCommand(team.Id, player, captain))).IsSuccess.Should().BeTrue();

        var back = await new AcceptTeamInvitationHandler(db, _clock).HandleAsync(new AcceptTeamInvitationCommand("ABC234", player));

        back.IsFailure.Should().BeTrue();
        back.Error.Code.Should().Be(TeamErrors.RemovedFromTeam.Code);
    }

    [Fact]
    public async Task AcceptHandler_RemovedPlayerInvitedAgainByLeader_CanJoinAsync()
    {
        using var db = CreateInMemoryDb();
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        await new AcceptTeamInvitationHandler(db, _clock).HandleAsync(new AcceptTeamInvitationCommand("ABC234", player));
        await new RemoveTeamMemberHandler(db, _clock).HandleAsync(new RemoveTeamMemberCommand(team.Id, player, captain));

        var invite = await new CreateTeamInvitationHandler(db, _clock, new TeamInviteCodeGenerator())
            .HandleAsync(new CreateTeamInvitationCommand(team.Id, captain, 7, player));
        invite.IsSuccess.Should().BeTrue();

        var back = await new AcceptTeamInvitationHandler(db, _clock).HandleAsync(new AcceptTeamInvitationCommand(invite.Value.InviteCode, player));

        back.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcceptHandler_PlayerWhoLeftVoluntarily_CanRejoinWithSameCodeAsync()
    {
        using var db = CreateInMemoryDb();
        var player = Guid.NewGuid();
        var team = NewTeam(Guid.NewGuid());
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        await new AcceptTeamInvitationHandler(db, _clock).HandleAsync(new AcceptTeamInvitationCommand("ABC234", player));
        (await new LeaveTeamHandler(db, _clock).HandleAsync(new LeaveTeamCommand(team.Id, player))).IsSuccess.Should().BeTrue();

        var back = await new AcceptTeamInvitationHandler(db, _clock).HandleAsync(new AcceptTeamInvitationCommand("ABC234", player));

        back.IsSuccess.Should().BeTrue();
    }

    // ---------- 2. Approval and the 5-teams limit ----------

    [Fact]
    public void ApproveJoinRequest_WhenRequesterAlreadyInFiveTeams_FailsAndRequestStaysPending()
    {
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        var request = team.CreateJoinRequest(player, null, 0, _clock.UtcNow).Value;

        var result = team.ApproveJoinRequest(request.Id, captain, _clock.UtcNow, requesterActiveTeamsCount: 5);

        result.Error.Code.Should().Be(TeamErrors.MaxPlayerTeamsExceeded.Code);
        request.Status.Should().Be(JoinRequestStatus.Pending);
        team.Members.Should().NotContain(m => m.PlayerId == player);
    }

    [Fact]
    public async Task ApproveHandler_WhenRequesterReachedFiveTeamsAfterRequesting_IsRefusedAsync()
    {
        using var db = CreateInMemoryDb();
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var target = NewTeam(captain, "TGT234", "target");
        var request = target.CreateJoinRequest(player, null, 0, _clock.UtcNow).Value;
        db.Teams.Add(target);
        for (var i = 0; i < 5; i++)
        {
            db.Teams.Add(NewTeam(player, $"OWN23{i}", $"own-{i}"));
        }

        await db.SaveChangesAsync();

        var result = await new ApproveJoinRequestHandler(db, _clock).HandleAsync(new ApproveJoinRequestCommand(target.Id, request.Id, captain));

        result.Error.Code.Should().Be(TeamErrors.MaxPlayerTeamsExceeded.Code);
        (await db.TeamMembers.CountAsync(m => m.PlayerId == player && m.IsActive)).Should().Be(5);
        (await db.TeamJoinRequests.SingleAsync(r => r.Id == request.Id)).Status.Should().Be(JoinRequestStatus.Pending);
    }

    [Fact]
    public async Task ApproveHandler_ForPlayerRemovedEarlier_AddsThemAndLiftsTheBlockAsync()
    {
        using var db = CreateInMemoryDb();
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        await new AcceptTeamInvitationHandler(db, _clock).HandleAsync(new AcceptTeamInvitationCommand("ABC234", player));
        await new RemoveTeamMemberHandler(db, _clock).HandleAsync(new RemoveTeamMemberCommand(team.Id, player, captain));
        var request = team.CreateJoinRequest(player, null, 0, _clock.UtcNow).Value;
        db.TeamJoinRequests.Add(request);
        await db.SaveChangesAsync();

        var result = await new ApproveJoinRequestHandler(db, _clock).HandleAsync(new ApproveJoinRequestCommand(team.Id, request.Id, captain));

        result.IsSuccess.Should().BeTrue();
        (await db.TeamMembers.IgnoreQueryFilters().AnyAsync(m => m.TeamId == team.Id && m.PlayerId == player && m.RejoinBlocked)).Should().BeFalse();
    }

    // ---------- 3. The limit counted at join time ----------

    [Fact]
    public void JoinWithInviteCode_WhenPlayerAlreadyInFiveTeams_ReturnsMaxPlayerTeamsExceeded()
    {
        var team = NewTeam(Guid.NewGuid());

        var result = team.JoinWithInviteCode("ABC234", Guid.NewGuid(), _clock.UtcNow, activeTeamsCount: 5);

        result.Error.Code.Should().Be(TeamErrors.MaxPlayerTeamsExceeded.Code);
    }

    // ---------- 4. Stale join requests ----------

    [Fact]
    public void JoinWithInviteCode_WithPendingRequest_CancelsTheRequest()
    {
        var player = Guid.NewGuid();
        var team = NewTeam(Guid.NewGuid());
        var request = team.CreateJoinRequest(player, null, 0, _clock.UtcNow).Value;

        team.JoinWithInviteCode("ABC234", player, _clock.UtcNow).IsSuccess.Should().BeTrue();

        request.Status.Should().Be(JoinRequestStatus.Cancelled);
    }

    [Fact]
    public void ApproveJoinRequest_WhenPlayerAlreadyActiveMember_ReturnsPlayerAlreadyInTeamAndKeepsRole()
    {
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        var request = team.CreateJoinRequest(player, null, 0, _clock.UtcNow).Value;
        team.JoinWithInviteCode("ABC234", player, _clock.UtcNow);
        team.UpdateMemberRole(player, TeamRole.ViceCaptain, captain, _clock.UtcNow).IsSuccess.Should().BeTrue();

        var result = team.ApproveJoinRequest(request.Id, captain, _clock.UtcNow);

        result.IsFailure.Should().BeTrue();
        team.Members.Single(m => m.PlayerId == player).Role.Should().Be(TeamRole.ViceCaptain);
    }

    [Fact]
    public async Task AcceptHandler_WithPendingRequest_RemovesItFromPendingListAsync()
    {
        using var db = CreateInMemoryDb();
        var player = Guid.NewGuid();
        var team = NewTeam(Guid.NewGuid());
        var request = team.CreateJoinRequest(player, null, 0, _clock.UtcNow).Value;
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        (await new AcceptTeamInvitationHandler(db, _clock).HandleAsync(new AcceptTeamInvitationCommand("ABC234", player))).IsSuccess.Should().BeTrue();

        (await db.TeamJoinRequests.AnyAsync(r => r.Id == request.Id && r.Status == JoinRequestStatus.Pending)).Should().BeFalse();
    }

    // ---------- 5. Lone captain ----------

    [Fact]
    public void LeaveTeam_LoneCaptain_DisbandsTheTeam()
    {
        var captain = Guid.NewGuid();
        var team = NewTeam(captain);
        var request = team.CreateJoinRequest(Guid.NewGuid(), null, 0, _clock.UtcNow).Value;

        var result = team.LeaveTeam(captain, _clock.UtcNow);

        result.IsSuccess.Should().BeTrue();
        team.IsDeleted.Should().BeTrue();
        team.Members.Should().OnlyContain(m => !m.IsActive);
        request.Status.Should().Be(JoinRequestStatus.Cancelled);
    }

    [Fact]
    public void LeaveTeam_CaptainWithOtherMembers_MustStillTransferCaptaincy()
    {
        var captain = Guid.NewGuid();
        var team = NewTeam(captain);
        team.JoinWithInviteCode("ABC234", Guid.NewGuid(), _clock.UtcNow);

        var result = team.LeaveTeam(captain, _clock.UtcNow);

        result.Error.Code.Should().Be(TeamErrors.SelfRemovalMustTransferCaptaincy.Code);
        team.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task LeaveHandler_LoneCaptain_HidesTeamAndFreesTheSlotAsync()
    {
        using var db = CreateInMemoryDb();
        var captain = Guid.NewGuid();
        var team = NewTeam(captain);
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var result = await new LeaveTeamHandler(db, _clock).HandleAsync(new LeaveTeamCommand(team.Id, captain));

        result.IsSuccess.Should().BeTrue();
        (await db.Teams.AnyAsync(t => t.Id == team.Id)).Should().BeFalse();
        (await db.TeamMembers.CountAsync(m => m.PlayerId == captain && m.IsActive)).Should().Be(0);
        (await new AcceptTeamInvitationHandler(db, _clock).HandleAsync(new AcceptTeamInvitationCommand("ABC234", Guid.NewGuid())))
            .Error.Code.Should().Be(TeamErrors.InvalidInviteCode.Code);
    }

    [Fact]
    public async Task CreateHandler_AfterDisbanding_DoesNotReuseTheDisbandedSlugAsync()
    {
        using var db = CreateInMemoryDb();
        var captain = Guid.NewGuid();
        var create = new CreateTeamHandler(db, _clock, new TeamSlugGenerator(), new TeamInviteCodeGenerator());
        var first = await create.HandleAsync(new CreateTeamCommand(captain, "Nile Stars", null, "Cairo", "Cairo"));
        await new LeaveTeamHandler(db, _clock).HandleAsync(new LeaveTeamCommand(first.Value.Id, captain));

        var second = await create.HandleAsync(new CreateTeamCommand(captain, "Nile Stars", null, "Cairo", "Cairo"));

        second.IsSuccess.Should().BeTrue();
        second.Value.Slug.Should().NotBe(first.Value.Slug);
    }

    // ---------- 6. Vice-captain ----------

    [Fact]
    public void UpdateMemberRole_CaptainAppointsViceCaptain_Succeeds()
    {
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        team.JoinWithInviteCode("ABC234", player, _clock.UtcNow);

        team.UpdateMemberRole(player, TeamRole.ViceCaptain, captain, _clock.UtcNow).IsSuccess.Should().BeTrue();

        team.Members.Single(m => m.PlayerId == player).Role.Should().Be(TeamRole.ViceCaptain);
    }

    [Theory]
    [InlineData(TeamRole.Captain)]
    [InlineData((TeamRole)99)]
    public void UpdateMemberRole_WithCaptainOrUnknownRole_ReturnsInvalidRole(TeamRole role)
    {
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        team.JoinWithInviteCode("ABC234", player, _clock.UtcNow);

        team.UpdateMemberRole(player, role, captain, _clock.UtcNow).Error.Code.Should().Be(TeamErrors.InvalidRole.Code);
        team.CaptainId.Should().Be(captain);
    }

    [Fact]
    public void UpdateMemberRole_ByNonCaptain_ReturnsNotCaptain()
    {
        var captain = Guid.NewGuid();
        var vice = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        team.JoinWithInviteCode("ABC234", vice, _clock.UtcNow);
        team.JoinWithInviteCode("ABC234", player, _clock.UtcNow);
        team.UpdateMemberRole(vice, TeamRole.ViceCaptain, captain, _clock.UtcNow);

        team.UpdateMemberRole(player, TeamRole.ViceCaptain, vice, _clock.UtcNow).Error.Code.Should().Be(TeamErrors.NotCaptain.Code);
    }

    [Fact]
    public async Task UpdateMemberRoleHandler_AppointsAndDemotesViceCaptainAsync()
    {
        using var db = CreateInMemoryDb();
        var captain = Guid.NewGuid();
        var player = Guid.NewGuid();
        var team = NewTeam(captain);
        team.JoinWithInviteCode("ABC234", player, _clock.UtcNow);
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        var handler = new UpdateMemberRoleHandler(db, _clock);

        (await handler.HandleAsync(new UpdateMemberRoleCommand(team.Id, player, TeamRole.ViceCaptain, captain))).IsSuccess.Should().BeTrue();
        (await db.TeamMembers.SingleAsync(m => m.PlayerId == player)).Role.Should().Be(TeamRole.ViceCaptain);

        (await handler.HandleAsync(new UpdateMemberRoleCommand(team.Id, player, TeamRole.Member, captain))).IsSuccess.Should().BeTrue();
        (await db.TeamMembers.SingleAsync(m => m.PlayerId == player)).Role.Should().Be(TeamRole.Member);
    }

    [Fact]
    public async Task UpdateMemberRoleHandler_UnknownTeam_ReturnsNotFoundAsync()
    {
        using var db = CreateInMemoryDb();

        var result = await new UpdateMemberRoleHandler(db, _clock)
            .HandleAsync(new UpdateMemberRoleCommand(Guid.NewGuid(), Guid.NewGuid(), TeamRole.ViceCaptain, Guid.NewGuid()));

        result.Error.Code.Should().Be(TeamErrors.NotFound.Code);
    }

    // ---------- 8. Suspended team ----------

    [Fact]
    public void ApproveJoinRequest_WhenTeamSuspended_ReturnsSuspended()
    {
        var captain = Guid.NewGuid();
        var team = NewTeam(captain);
        var request = team.CreateJoinRequest(Guid.NewGuid(), null, 0, _clock.UtcNow).Value;
        team.Suspend("abuse", _clock.UtcNow);

        var result = team.ApproveJoinRequest(request.Id, captain, _clock.UtcNow);

        result.Error.Code.Should().Be(TeamErrors.Suspended.Code);
        request.Status.Should().Be(JoinRequestStatus.Pending);
    }

    // ---------- 9. Privacy at creation ----------

    [Fact]
    public async Task CreateHandler_WithPrivacyFlags_AppliesThemInOneCallAsync()
    {
        using var db = CreateInMemoryDb();
        var handler = new CreateTeamHandler(db, _clock, new TeamSlugGenerator(), new TeamInviteCodeGenerator());

        var result = await handler.HandleAsync(new CreateTeamCommand(
            Guid.NewGuid(), "Private FC", null, "Cairo", "Cairo", IsPublic: false, IsRecruiting: false, IsOpenForChallenges: false));

        result.IsSuccess.Should().BeTrue();
        var team = await db.Teams.SingleAsync();
        team.IsPublic.Should().BeFalse();
        team.IsRecruiting.Should().BeFalse();
        team.IsOpenForChallenges.Should().BeFalse();
    }

    [Fact]
    public async Task CreateHandler_WithoutPrivacyFlags_KeepsPublicRecruitingDefaultsAsync()
    {
        using var db = CreateInMemoryDb();
        var handler = new CreateTeamHandler(db, _clock, new TeamSlugGenerator(), new TeamInviteCodeGenerator());

        (await handler.HandleAsync(new CreateTeamCommand(Guid.NewGuid(), "Open FC", null, "Cairo", "Cairo"))).IsSuccess.Should().BeTrue();

        var team = await db.Teams.SingleAsync();
        team.IsPublic.Should().BeTrue();
        team.IsRecruiting.Should().BeTrue();
        team.IsOpenForChallenges.Should().BeTrue();
    }
}
