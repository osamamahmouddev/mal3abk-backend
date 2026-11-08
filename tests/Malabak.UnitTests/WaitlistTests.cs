using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Waitlist;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class WaitlistTests
{
    private static readonly DateTime _now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task JoinAsync_CreatesEncryptedEntry_WithBlindIndexAndConsentAsync()
    {
        await using var db = NewDb();
        var cipher = new FakeFieldCipher();
        var blindIndex = new FakeBlindIndexService();
        var normalizer = new PhoneNumberNormalizer();
        var clock = new FakeClock();

        var service = new WaitlistService(
            db, normalizer, blindIndex, cipher, clock, NullLogger<WaitlistService>.Instance);

        var result = await service.JoinAsync("01012345678", WaitlistAudience.Player, null, "192.168.1.1", "Mozilla/5.0");

        result.IsSuccess.Should().BeTrue();
        result.Value.IsNew.Should().BeTrue();

        var entry = await db.WaitlistEntries.SingleAsync();
        entry.Audience.Should().Be(WaitlistAudience.Player);
        entry.ChosenPlan.Should().BeNull();
        entry.PhoneBlindIndex.Should().NotBeEmpty();
        entry.PhoneEncrypted.Should().NotBeEmpty();
        entry.ConsentRecordedAtUtc.Should().Be(_now);
        entry.IpAddress.Should().Be("192.168.1.1");
    }

    [Fact]
    public async Task JoinAsync_WithExistingPhone_UpdatesPlanAndAudienceIdempotentlyAsync()
    {
        await using var db = NewDb();
        var cipher = new FakeFieldCipher();
        var blindIndex = new FakeBlindIndexService();
        var normalizer = new PhoneNumberNormalizer();
        var clock = new FakeClock();

        var service = new WaitlistService(
            db, normalizer, blindIndex, cipher, clock, NullLogger<WaitlistService>.Instance);

        var first = await service.JoinAsync("01012345678", WaitlistAudience.Player, null, "1.1.1.1", "Agent");
        first.IsSuccess.Should().BeTrue();
        first.Value.IsNew.Should().BeTrue();

        var second = await service.JoinAsync("01012345678", WaitlistAudience.Venue, "Growth", "1.1.1.2", "Agent2");
        second.IsSuccess.Should().BeTrue();
        second.Value.IsNew.Should().BeFalse();
        second.Value.Id.Should().Be(first.Value.Id);

        var count = await db.WaitlistEntries.CountAsync();
        count.Should().Be(1);

        var entry = await db.WaitlistEntries.SingleAsync();
        entry.Audience.Should().Be(WaitlistAudience.Venue);
        entry.ChosenPlan.Should().Be("Growth");
    }

    [Fact]
    public async Task JoinAsync_InvalidPhone_FailsValidationAsync()
    {
        await using var db = NewDb();
        var cipher = new FakeFieldCipher();
        var blindIndex = new FakeBlindIndexService();
        var normalizer = new PhoneNumberNormalizer();
        var clock = new FakeClock();

        var service = new WaitlistService(
            db, normalizer, blindIndex, cipher, clock, NullLogger<WaitlistService>.Instance);

        var result = await service.JoinAsync("12345", WaitlistAudience.Player, null, null, null);

        result.IsFailure.Should().BeTrue();
        var count = await db.WaitlistEntries.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task ListAsync_ReturnsDecryptedPhone_AndFiltersByAudienceAndPlanAsync()
    {
        await using var db = NewDb();
        var cipher = new FakeFieldCipher();
        var blindIndex = new FakeBlindIndexService();
        var normalizer = new PhoneNumberNormalizer();
        var clock = new FakeClock();

        var service = new WaitlistService(
            db, normalizer, blindIndex, cipher, clock, NullLogger<WaitlistService>.Instance);

        await service.JoinAsync("01011111111", WaitlistAudience.Player, null, null, null);
        await service.JoinAsync("01022222222", WaitlistAudience.Venue, "Starter", null, null);
        await service.JoinAsync("01033333333", WaitlistAudience.Venue, "Growth", null, null);

        var all = (await service.ListAsync(null, null, 1, 10)).Value;
        all.TotalCount.Should().Be(3);
        all.Items.Should().HaveCount(3);
        all.Items.Select(i => i.Phone).Should().Contain("+201011111111");

        var venues = (await service.ListAsync(WaitlistAudience.Venue, null, 1, 10)).Value;
        venues.TotalCount.Should().Be(2);

        var growth = (await service.ListAsync(WaitlistAudience.Venue, "Growth", 1, 10)).Value;
        growth.TotalCount.Should().Be(1);
        growth.Items.Single().ChosenPlan.Should().Be("Growth");
    }

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeClock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }

    private sealed class FakeFieldCipher : IFieldCipher
    {
        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(System.Text.Encoding.UTF8.GetBytes("ENC:" + plaintext));

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default)
        {
            var str = System.Text.Encoding.UTF8.GetString(ciphertext);
            return ValueTask.FromResult(str.StartsWith("ENC:") ? str[4..] : str);
        }

        public ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class FakeBlindIndexService : IBlindIndexService
    {
        public byte[] Compute(string normalizedValue, string purpose) =>
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(purpose + ":" + normalizedValue));
    }
}
