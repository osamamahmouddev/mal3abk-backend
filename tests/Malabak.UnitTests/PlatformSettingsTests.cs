using FluentAssertions;
using Malabak.Api.Infrastructure.Platform;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

[Collection(nameof(PlatformSettingsTests))]
public sealed class PlatformSettingsTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _admin = Guid.NewGuid();
    private readonly RecordingAudit _audit = new();
    private readonly MutableClock _clock = new();

    [Theory]
    [InlineData(PlatformSettingKeys.HoldMinutes, "45", "45")]
    [InlineData(PlatformSettingKeys.HoldMinutes, " 30 ", "30")]
    [InlineData(PlatformSettingKeys.HoldMinutes, "4", null)]
    [InlineData(PlatformSettingKeys.HoldMinutes, "500", null)]
    [InlineData(PlatformSettingKeys.HoldMinutes, "abc", null)]
    [InlineData(PlatformSettingKeys.MaintenanceEnabled, "True", "true")]
    [InlineData(PlatformSettingKeys.MaintenanceEnabled, "yes", null)]
    [InlineData(PlatformSettingKeys.MaintenanceMessage, "", null)]
    public void Values_AreValidatedAgainstTheirTypeAndRange(string key, string value, string? expected)
    {
        PlatformCatalog.Normalize(PlatformCatalog.Setting(key)!, value).Should().Be(expected);
    }

    [Fact]
    public async Task Defaults_Apply_UntilAnAdminChangesASetting_AndTheChangeIsAuditedAsync()
    {
        await using var db = NewDb();
        var service = new PlatformService(db, _audit, _clock);

        (await service.GetIntAsync(PlatformSettingKeys.HoldMinutes)).Should().Be(30);

        var updated = (await service.UpdateSettingAsync(PlatformSettingKeys.HoldMinutes, "45", _admin, "Players asked for more time")).Value;

        updated.Should().Match<SettingDto>(s => s.Value == "45" && !s.IsDefault && s.UpdatedBy == _admin);
        (await service.GetIntAsync(PlatformSettingKeys.HoldMinutes)).Should().Be(45, "the writer's cache is cleared at once");
        _audit.Entries.Should().ContainSingle().Which.Should().Match<AuditEntry>(e =>
            e.Action == "platform.setting_changed" && e.EntityId == PlatformSettingKeys.HoldMinutes && e.ActorId == _admin && e.Reason == "Players asked for more time");
    }

    [Fact]
    public async Task InvalidOrUnknownChanges_AreRefused_WithoutTouchingTheStoredValueAsync()
    {
        await using var db = NewDb();
        var service = new PlatformService(db, _audit, _clock);

        (await service.UpdateSettingAsync(PlatformSettingKeys.HoldMinutes, "1000", _admin, null)).Error.Should().Be(PlatformErrors.InvalidValue);
        (await service.UpdateSettingAsync("nope", "1", _admin, null)).Error.Should().Be(PlatformErrors.UnknownSetting);
        (await service.UpdateFlagAsync("nope", true, _admin, null)).Error.Should().Be(PlatformErrors.UnknownFlag);
        (await db.Settings.CountAsync()).Should().Be(0);
        _audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Flags_AreOffByDefault_AndTheStatusListsOnlyEnabledPublicOnesAsync()
    {
        await using var db = NewDb();
        var service = new PlatformService(db, _audit, _clock);

        (await service.IsEnabledAsync(FeatureFlagKeys.Reviews)).Should().BeFalse();
        (await service.IsEnabledAsync("not-a-flag")).Should().BeFalse();

        await service.UpdateFlagAsync(FeatureFlagKeys.Reviews, true, _admin, null);
        await service.UpdateFlagAsync(FeatureFlagKeys.PaymentGateway, true, _admin, null);
        await service.UpdateSettingAsync(PlatformSettingKeys.MaintenanceEnabled, "true", _admin, null);

        var status = await service.StatusAsync();
        status.Maintenance.Should().BeTrue();
        status.Message.Should().NotBeNullOrWhiteSpace();
        status.EnabledFeatures.Should().Equal(FeatureFlagKeys.Reviews);
    }

    [Fact]
    public async Task OtherInstancesSeeAChange_WithinTheCacheLifetimeAsync()
    {
        await using var db = NewDb();
        var reader = new PlatformService(db, _audit, _clock);
        (await reader.IsEnabledAsync(FeatureFlagKeys.Tournaments)).Should().BeFalse();

        db.Flags.Add(new FeatureFlag { Key = FeatureFlagKeys.Tournaments, Enabled = true, UpdatedAtUtc = _now });
        await db.SaveChangesAsync();

        (await reader.IsEnabledAsync(FeatureFlagKeys.Tournaments)).Should().BeFalse("still cached");
        _clock.UtcNow += PlatformService.CacheLifetime;
        (await reader.IsEnabledAsync(FeatureFlagKeys.Tournaments)).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/bookings", false)]
    [InlineData("/api/v1/venues/explore", false)]
    [InlineData("/hubs/booking", false)]
    [InlineData("/api/v1/admin/venues/pending-approval", true)]
    [InlineData("/api/v1/admin/auth/login", true)]
    [InlineData("/api/v1/platform/status", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/swagger/v1/swagger.json", true)]
    public void MaintenanceMode_KeepsAdminStatusAndHealthReachable(string path, bool exempt)
    {
        MaintenanceModeMiddleware.IsExempt(new PathString(path)).Should().Be(exempt);
    }

    private static PlatformDbContext NewDb() => new(new DbContextOptionsBuilder<PlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class RecordingAudit : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableClock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow { get; set; } = _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
