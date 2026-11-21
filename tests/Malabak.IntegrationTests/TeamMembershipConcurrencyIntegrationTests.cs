using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Malabak.IntegrationTests;

/// <summary>
/// The team limits must hold when requests arrive at the same moment, on a real PostgreSQL: a player can never pass
/// five teams and a roster can never pass 25 members, however many joins race.
/// </summary>
public sealed class TeamMembershipConcurrencyIntegrationTests
{
    private static readonly DateTime _nowUtc = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<TeamsDbContext> Options() =>
        new DbContextOptionsBuilder<TeamsDbContext>()
            .UseNpgsql(IntegrationTestEnvironment.PostgresConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TeamsDbContext.Schema))
            .Options;

    private static async Task MigrateAsync()
    {
        await using var db = new TeamsDbContext(Options());
        await db.Database.MigrateAsync();
    }

    // Invite codes only use the unambiguous alphabet (no 0/O, 1/I), so a random hex code is sometimes refused.
    private static string Code() =>
        new(Enumerable.Range(0, TeamInviteCodes.Length).Select(_ => TeamInviteCodes.Alphabet[Random.Shared.Next(TeamInviteCodes.Alphabet.Length)]).ToArray());

    private static Team NewTeam(Guid captainId, string code) =>
        Team.Create(Guid.NewGuid(), "Race FC", $"race-{Guid.NewGuid():N}", "Cairo", "Cairo", captainId, code, _nowUtc).Value;

    [Fact]
    public async Task AcceptInvitation_PlayerInFourTeamsJoiningThreeAtOnce_EndsInExactlyFiveTeamsAsync()
    {
        await MigrateAsync();
        var racer = Guid.NewGuid();
        var codes = new[] { Code(), Code(), Code() };
        await using (var seed = new TeamsDbContext(Options()))
        {
            for (var i = 0; i < 4; i++)
            {
                seed.Teams.Add(NewTeam(racer, Code()));
            }

            foreach (var code in codes)
            {
                seed.Teams.Add(NewTeam(Guid.NewGuid(), code));
            }

            await seed.SaveChangesAsync();
        }

        var results = await Task.WhenAll(codes.Select(async code =>
        {
            await using var db = new TeamsDbContext(Options());
            return await new AcceptTeamInvitationHandler(db, new FakeClock(_nowUtc)).HandleAsync(new AcceptTeamInvitationCommand(code, racer));
        }));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Where(r => r.IsFailure).Should().OnlyContain(r => r.Error.Code == TeamErrors.MaxPlayerTeamsExceeded.Code);
        await using var check = new TeamsDbContext(Options());
        (await check.TeamMembers.CountAsync(m => m.PlayerId == racer && m.IsActive)).Should().Be(Team.MaxTeamsPerPlayer);
    }

    [Fact]
    public async Task AcceptInvitation_FiveJoinsRacingForTheLastRosterSpot_OnlyOneGetsInAsync()
    {
        await MigrateAsync();
        var code = Code();
        var team = NewTeam(Guid.NewGuid(), code);
        for (var i = 1; i < Team.MaxTeamMembers - 1; i++)
        {
            team.JoinWithInviteCode(code, Guid.NewGuid(), _nowUtc).IsSuccess.Should().BeTrue();
        }

        await using (var seed = new TeamsDbContext(Options()))
        {
            seed.Teams.Add(team);
            await seed.SaveChangesAsync();
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var db = new TeamsDbContext(Options());
            return await new AcceptTeamInvitationHandler(db, new FakeClock(_nowUtc)).HandleAsync(new AcceptTeamInvitationCommand(code, Guid.NewGuid()));
        }));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Where(r => r.IsFailure).Should().OnlyContain(r => r.Error.Code == TeamErrors.TeamRosterFull.Code);
        await using var check = new TeamsDbContext(Options());
        (await check.TeamMembers.CountAsync(m => m.TeamId == team.Id && m.IsActive)).Should().Be(Team.MaxTeamMembers);
    }

    [Fact]
    public async Task TeamRowVersion_IsEnforcedAsXminOnPostgresAsync()
    {
        await MigrateAsync();
        var team = NewTeam(Guid.NewGuid(), Code());
        await using (var seed = new TeamsDbContext(Options()))
        {
            seed.Teams.Add(team);
            await seed.SaveChangesAsync();
        }

        await using var first = new TeamsDbContext(Options());
        await using var second = new TeamsDbContext(Options());
        var a = await first.Teams.Include(t => t.Members).SingleAsync(t => t.Id == team.Id);
        var b = await second.Teams.Include(t => t.Members).SingleAsync(t => t.Id == team.Id);
        a.UpdateSettings(false, true, true, team.CaptainId, _nowUtc);
        b.UpdateSettings(true, false, true, team.CaptainId, _nowUtc);
        await first.SaveChangesAsync();

        var stale = () => second.SaveChangesAsync();

        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task InviteCode_IsUniqueInTheDatabaseAsync()
    {
        await MigrateAsync();
        var code = Code();
        await using (var seed = new TeamsDbContext(Options()))
        {
            seed.Teams.Add(NewTeam(Guid.NewGuid(), code));
            await seed.SaveChangesAsync();
        }

        await using var db = new TeamsDbContext(Options());
        db.Teams.Add(NewTeam(Guid.NewGuid(), code));
        var duplicate = () => db.SaveChangesAsync();

        (await duplicate.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("IX_teams_InviteCode");
    }
}
