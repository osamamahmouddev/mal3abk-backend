using System.Text.Json;
using FluentAssertions;
using Malabak.Modules.Audit.Application;
using Malabak.Modules.Audit.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class AuditLogTests
{
    private static readonly DateTime _now = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Masking_HidesSecrets_AndKeepsOnlyTheEndOfContactDetails_AtAnyDepth()
    {
        var json = AuditMasking.Serialize(new
        {
            Name = "Karim",
            PasswordHash = "abc",
            Phone = "+201012345678",
            Nested = new { RefreshToken = "t", Email = "karim@example.com", Items = new[] { new { AccountNumber = "EG380019000500000000263180002" } } },
        })!;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("name").GetString().Should().Be("Karim");
        root.GetProperty("passwordHash").GetString().Should().Be(AuditMasking.Masked);
        root.GetProperty("phone").GetString().Should().Be("***5678");
        root.GetProperty("nested").GetProperty("refreshToken").GetString().Should().Be(AuditMasking.Masked);
        root.GetProperty("nested").GetProperty("email").GetString().Should().Be("***.com");
        root.GetProperty("nested").GetProperty("items")[0].GetProperty("accountNumber").GetString().Should().Be("***0002");
        json.Should().NotContain("1012345678").And.NotContain("karim@");
    }

    [Fact]
    public async Task Recording_StoresActorEntitySnapshotsAndRequestFactsAsync()
    {
        await using var db = NewDb();
        var admin = Guid.NewGuid();
        var service = new AuditLogService(db, new FakeRequest("41.33.0.1", admin, "trace-1"), new FixedClock(), NullLogger<AuditLogService>.Instance);

        await service.RecordAsync(new AuditEntry("venue.suspended", "Venue", "v-1", admin, AuditActorType.Admin, new { Status = "Active" }, new { Status = "Suspended" }, "Fake listing"));

        var row = await db.Entries.SingleAsync();
        row.Should().BeEquivalentTo(new
        {
            OccurredAtUtc = _now,
            ActorId = (Guid?)admin,
            ActorType = "Admin",
            ImpersonatorId = (Guid?)admin,
            Action = "venue.suspended",
            EntityType = "Venue",
            EntityId = "v-1",
            Reason = "Fake listing",
            IpAddress = "41.33.0.1",
            CorrelationId = "trace-1",
        });
        row.BeforeJson.Should().Contain("Active");
        row.AfterJson.Should().Contain("Suspended");
    }

    [Fact]
    public async Task AFailedWrite_NeverBreaksTheAuditedOperationAsync()
    {
        var db = NewDb();
        await db.DisposeAsync();
        var service = new AuditLogService(db, new FakeRequest(null, null, null), new FixedClock(), NullLogger<AuditLogService>.Instance);

        var act = () => service.RecordAsync(new AuditEntry("x", "Y", "1", null, AuditActorType.System));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Search_FiltersNewestFirst_ByActorOrImpersonator_ActionPrefix_EntityAndTimeAsync()
    {
        await using var db = NewDb();
        var admin = Guid.NewGuid();
        var other = Guid.NewGuid();
        var clock = new FixedClock();
        var asAdmin = new AuditLogService(db, new FakeRequest(null, null, null), clock, NullLogger<AuditLogService>.Instance);
        var impersonating = new AuditLogService(db, new FakeRequest(null, admin, null), clock, NullLogger<AuditLogService>.Instance);

        await asAdmin.RecordAsync(new AuditEntry("venue.approved", "Venue", "v-1", admin, AuditActorType.Admin));
        clock.UtcNow = _now.AddMinutes(1);
        await impersonating.RecordAsync(new AuditEntry("booking.cancelled", "Booking", "b-1", other, AuditActorType.User));
        clock.UtcNow = _now.AddMinutes(2);
        await asAdmin.RecordAsync(new AuditEntry("venue.suspended", "Venue", "v-2", other, AuditActorType.Admin));
        var query = new AuditLogQuery(db);

        (await query.SearchAsync(new AuditLogFilter(ActorId: admin))).Value.Items.Select(i => i.EntityId).Should().Equal("b-1", "v-1");
        (await query.SearchAsync(new AuditLogFilter(Action: "venue."))).Value.Items.Select(i => i.EntityId).Should().Equal("v-2", "v-1");
        (await query.SearchAsync(new AuditLogFilter(EntityType: "Booking", EntityId: "b-1"))).Value.TotalCount.Should().Be(1);
        (await query.SearchAsync(new AuditLogFilter(FromUtc: _now.AddMinutes(1)))).Value.TotalCount.Should().Be(2);
        (await query.SearchAsync(new AuditLogFilter(PageSize: 500))).Error.Should().Be(AuditLogErrors.InvalidFilter);
        (await query.SearchAsync(new AuditLogFilter(FromUtc: _now, ToUtc: _now.AddDays(-1)))).Error.Should().Be(AuditLogErrors.InvalidFilter);
    }

    private static AuditDbContext NewDb() => new(new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record FakeRequest(string? IpAddress, Guid? ImpersonatorId, string? CorrelationId) : IAuditRequestContext;

    private sealed class FixedClock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow { get; set; } = _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
