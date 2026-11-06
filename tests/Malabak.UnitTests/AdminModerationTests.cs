using FluentAssertions;
using Malabak.Modules.Matches.Application.Admin;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.Modules.Matches.Infrastructure.Jobs;
using Malabak.Modules.Teams.Application.Admin;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Teams.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class AdminModerationTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _captain = Guid.NewGuid();
    private readonly Recorder _recorder = new();
    private readonly Clock _clock = new();

    [Fact]
    public async Task SuspendingATeam_BlocksCaptainActionsChallengesAndNewMembers_UntilReactivatedAsync()
    {
        await using var db = TeamsDb();
        var team = Team.Create(Guid.NewGuid(), "Cairo Lions", "cairo-lions", "Cairo", "Cairo", _captain, "INV001", _now).Value;
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        var service = new AdminTeamModerationService(db, _recorder, _recorder, _clock);
        var checker = new TeamCaptaincyChecker(db);
        (await checker.IsCaptainOrViceCaptainAsync(team.Id, _captain)).Should().BeTrue();

        (await service.SuspendAsync(team.Id, " ", _admin)).IsFailure.Should().BeTrue();
        var suspended = (await service.SuspendAsync(team.Id, "Repeated violent conduct", _admin)).Value;

        suspended.IsSuspended.Should().BeTrue();
        (await checker.IsCaptainOrViceCaptainAsync(team.Id, _captain)).Should().BeFalse("captains of a suspended team cannot act in matches");
        (await checker.CanBeChallengedAsync(team.Id)).Should().BeFalse();
        var tracked = await db.Teams.Include(t => t.Members).SingleAsync();
        tracked.JoinWithInviteCode("INV001", Guid.NewGuid(), _now).Error.Should().Be(TeamErrors.Suspended);
        tracked.UpdateSettings(true, isRecruiting: true, isOpenForChallenges: false, _captain, _now).Error.Should().Be(TeamErrors.Suspended);
        _recorder.Notifications.Should().ContainSingle(n => n.Type == NotificationTypes.TeamSuspended && n.UserId == _captain);
        _recorder.Audits.Should().ContainSingle(a => a.Action == "admin.team.suspended" && a.Reason == "Repeated violent conduct");

        (await service.ReactivateAsync(team.Id, "Appeal accepted", _admin)).Value.IsSuspended.Should().BeFalse();
        (await checker.IsCaptainOrViceCaptainAsync(team.Id, _captain)).Should().BeTrue();
        (await service.SearchAsync("lions", suspended: false, 1, 25)).Value.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task CorrectingAResult_OnlyWorksOnConfirmedMatches_ReplaysRankings_AndTellsBothCaptainsAsync()
    {
        await using var db = MatchesDb();
        var home = Guid.NewGuid();
        var away = Guid.NewGuid();
        var awayCaptain = Guid.NewGuid();
        var confirmed = Match.Schedule(null, home, away, _now.AddDays(-2), 60, null, null, null, null, null, _now.AddDays(-3)).Value;
        confirmed.SubmitResult(2, 1, home, _captain, _now.AddDays(-2).AddHours(2));
        confirmed.VerifyResult(MatchVerificationAction.Confirm, away, awayCaptain, null, _now.AddDays(-2).AddHours(3)).IsSuccess.Should().BeTrue();
        var pending = Match.Schedule(null, home, away, _now.AddDays(-1), 60, null, null, null, null, null, _now.AddDays(-2)).Value;
        pending.SubmitResult(0, 0, home, _captain, _now.AddHours(-20));
        db.Matches.AddRange(confirmed, pending);
        await db.SaveChangesAsync();
        var recalculator = new CountingRecalculator();
        var service = new AdminMatchModerationService(db, new RankingRecalculationJob(db, recalculator, _clock, NullLogger<RankingRecalculationJob>.Instance),
            new Captains(home, _captain, away, awayCaptain), _recorder, _recorder, _clock);

        (await service.CorrectResultAsync(pending.Id, 1, 0, "late sheet", _admin)).Error.Code.Should().Be("Match.ResultNotConfirmed");
        (await service.CorrectResultAsync(confirmed.Id, -1, 0, "typo", _admin)).IsFailure.Should().BeTrue();

        var dto = (await service.CorrectResultAsync(confirmed.Id, 1, 3, "Referee sheet shows 1-3", _admin)).Value;

        dto.Should().NotBeNull();
        var saved = await db.Matches.SingleAsync(m => m.Id == confirmed.Id);
        saved.HomeScore.Should().Be(1);
        saved.Outcome.Should().Be(MatchOutcome.AwayWin);
        recalculator.Calls.Should().Be(1);
        recalculator.LastMatches.Should().ContainSingle(m => m.MatchId == confirmed.Id && m.HomeScore == 1 && m.AwayScore == 3);
        _recorder.Notifications.Where(n => n.Type == NotificationTypes.MatchResultCorrected).Select(n => n.UserId).Should().BeEquivalentTo([_captain, awayCaptain]);
        _recorder.Audits.Should().ContainSingle(a => a.Action == "admin.match.result_corrected");
        (await db.MatchResultAuditRecords.CountAsync(r => r.MatchId == confirmed.Id && r.ChangeType == "AdminCorrection")).Should().Be(1);
    }

    private static TeamsDbContext TeamsDb() => new(new DbContextOptionsBuilder<TeamsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MatchesDbContext MatchesDb() => new(new DbContextOptionsBuilder<MatchesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class CountingRecalculator : ITeamRankingRecalculator
    {
        public int Calls { get; private set; }
        public IReadOnlyList<ConfirmedMatchRecord> LastMatches { get; private set; } = [];

        public Task<Result> RecalculateAllAsync(IReadOnlyList<ConfirmedMatchRecord> confirmedMatches, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastMatches = confirmedMatches;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class Captains(Guid home, Guid homeCaptain, Guid away, Guid awayCaptain) : ITeamCaptaincyChecker
    {
        public Task<bool> IsCaptainOrViceCaptainAsync(Guid teamId, Guid playerId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<Guid?> GetCaptainPlayerIdAsync(Guid teamId, CancellationToken cancellationToken = default)
        {
            Guid? captain = null;
            if (teamId == home)
            {
                captain = homeCaptain;
            }
            else if (teamId == away)
            {
                captain = awayCaptain;
            }

            return Task.FromResult(captain);
        }

        public Task<string?> GetTeamNameAsync(Guid teamId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Team");

        public Task<bool> CanBeChallengedAsync(Guid teamId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class Recorder : IAuditLog, INotificationPublisher
    {
        public List<AuditEntry> Audits { get; } = [];
        public List<NotificationRequest> Notifications { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Audits.Add(entry);
            return Task.CompletedTask;
        }

        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            Notifications.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class Clock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
