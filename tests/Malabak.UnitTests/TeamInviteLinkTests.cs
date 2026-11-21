using FluentAssertions;
using Malabak.Api.Endpoints;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Invitations.PreviewTeamInvitation;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>A shared invite link only ever shows the team and leaves joining to the player.</summary>
public sealed class TeamInviteLinkTests
{
    private static readonly DateTime _now = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);
    private readonly FakeClock _clock = new(_now);
    private readonly Guid _captain = Guid.NewGuid();

    [Theory]
    [InlineData(" bjyflu ", "BJYFLU")]
    [InlineData("ABC234", "ABC234")]
    public void Codes_AreNormalized(string input, string expected) => TeamInviteCodes.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABC12")]
    [InlineData("ABC1234")]
    [InlineData("ABCDE0")]
    [InlineData("ABCDEI")]
    [InlineData("<scrip")]
    [InlineData("AB/../")]
    public void MalformedCodes_AreRejected(string? input) => TeamInviteCodes.Normalize(input).Should().BeNull();

    [Fact]
    public async Task Preview_ShowsPublicFacts_AndDoesNotJoinAsync()
    {
        await using var db = Db();
        var team = await SeedAsync(db);
        var player = Guid.NewGuid();

        var preview = (await Preview(db).HandleAsync(new PreviewTeamInvitationQuery("bjyflu", player))).Value;

        preview.TeamId.Should().Be(team.Id);
        preview.Name.Should().Be("Shobra Lions");
        preview.MemberCount.Should().Be(1);
        preview.MaxMembers.Should().Be(Team.MaxTeamMembers);
        preview.IsAlreadyMember.Should().BeFalse();
        (await db.TeamMembers.CountAsync(m => m.PlayerId == player)).Should().Be(0, "looking at an invite never joins");
        typeof(TeamInvitePreviewDto).GetProperties().Select(p => p.Name).Should().NotContain(["InviteCode", "Members", "CaptainId"]);
    }

    [Fact]
    public async Task Preview_TellsAMemberTheyAreAlreadyInAsync()
    {
        await using var db = Db();
        await SeedAsync(db);

        (await Preview(db).HandleAsync(new PreviewTeamInvitationQuery("BJYFLU", _captain))).Value.IsAlreadyMember.Should().BeTrue();
    }

    [Theory]
    [InlineData("ZZZZZZ")]
    [InlineData("bad")]
    public async Task UnknownOrMalformedCodes_AnswerTheSameAsync(string code)
    {
        await using var db = Db();
        await SeedAsync(db);

        (await Preview(db).HandleAsync(new PreviewTeamInvitationQuery(code, Guid.NewGuid()))).Error.Should().Be(TeamErrors.InvalidInviteCode);
    }

    [Fact]
    public async Task ExpiredAndSuspendedTeams_AreNotPreviewedAsync()
    {
        await using var db = Db();
        await SeedAsync(db, expires: _now.AddMinutes(-1));
        (await Preview(db).HandleAsync(new PreviewTeamInvitationQuery("BJYFLU", Guid.NewGuid()))).Error.Should().Be(TeamErrors.InviteCodeExpired);

        await using var db2 = Db();
        var suspended = await SeedAsync(db2);
        suspended.Suspend("abuse", _now);
        await db2.SaveChangesAsync();
        (await Preview(db2).HandleAsync(new PreviewTeamInvitationQuery("BJYFLU", Guid.NewGuid()))).Error.Should().Be(TeamErrors.InvalidInviteCode);
    }

    [Fact]
    public async Task Accept_RejectsMalformedCodesBeforeLookingThemUpAsync()
    {
        await using var db = Db();
        await SeedAsync(db);

        var result = await new AcceptTeamInvitationHandler(db, _clock).HandleAsync(new AcceptTeamInvitationCommand("' OR 1=1 --", Guid.NewGuid()));

        result.Error.Should().Be(TeamErrors.InvalidInviteCode);
    }

    [Fact]
    public void LandingPage_EchoesOnlyTheCode_AndOpensTheApp()
    {
        var html = InviteLinkEndpoints.Page("BJYFLU");

        html.Should().Contain("malabak://app/join/BJYFLU");
        html.Should().NotContain("<script", "the page runs no script");
    }

    private PreviewTeamInvitationHandler Preview(TeamsDbContext db) => new(db, _clock);

    private static TeamsDbContext Db() =>
        new(new DbContextOptionsBuilder<TeamsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private async Task<Team> SeedAsync(TeamsDbContext db, DateTime? expires = null)
    {
        var team = Team.Create(Guid.NewGuid(), "Shobra Lions", $"lions-{Guid.NewGuid():N}", "Cairo", "Cairo", _captain, "BJYFLU", _now,
            inviteCodeExpiresAtUtc: expires ?? _now.AddDays(7)).Value;
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        return team;
    }
}
