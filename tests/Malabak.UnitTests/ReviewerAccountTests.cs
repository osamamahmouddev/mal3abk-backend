using FluentAssertions;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// The App Store / Google Play review account (+201000000999, fixed code) must always let a reviewer in: retries on several
/// devices are never rate-limited, no SMS is sent, and an account the reviewer deleted while testing comes back on sign-in.
/// </summary>
public sealed class ReviewerAccountTests
{
    private const string _reviewer = "+201000000999";
    private readonly IPhoneNumberNormalizer _normalizer = new PhoneNumberNormalizer();
    private readonly IOtpSecurityService _security =
        new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));

    [Fact]
    public async Task SendingTheCode_IsNeverRateLimited_AndSendsNoSmsAsync()
    {
        var sender = new CountingSender();
        var handler = new SendOtpHandler(_normalizer, TestSecrets.CreateDevelopmentTurnstileValidator(), _security, new InMemoryOtpStore(), sender,
            NullLogger<SendOtpHandler>.Instance);

        for (var i = 0; i < 10; i++)
        {
            (await handler.HandleAsync(new SendOtpCommand("01000000999", "dev-mobile-turnstile"))).IsSuccess.Should().BeTrue($"attempt {i + 1}");
        }

        sender.Sent.Should().Be(0);
    }

    [Fact]
    public async Task AReviewAccountDeletedDuringReview_IsRestoredOnSignInAsync()
    {
        await using var db = Db();
        var clock = new FakeClock();
        var user = User.CreatePlayer(Guid.NewGuid(), _reviewer, "Apple Reviewer", clock.UtcNow.AddDays(-3));
        user.RequestDeletion(clock.UtcNow.AddDays(-1));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var handler = new VerifyOtpHandler(_normalizer, _security, new InMemoryOtpStore(), db, clock, NullLogger<VerifyOtpHandler>.Instance);

        var result = await handler.HandleAsync(new VerifyOtpCommand(_reviewer, "123456"));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.UserId.Should().Be(user.Id);
        (await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id)).IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task AnyOtherDeletedAccount_StillNeedsAnExplicitRestoreAsync()
    {
        await using var db = Db();
        var clock = new FakeClock();
        var user = User.CreatePlayer(Guid.NewGuid(), "+201012345678", "Player", clock.UtcNow.AddDays(-3));
        user.RequestDeletion(clock.UtcNow.AddDays(-1));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var store = new InMemoryOtpStore();
        await store.SaveOtpAsync("+201012345678", _security.ComputeHmacHash("654321"), TimeSpan.FromMinutes(5));
        var handler = new VerifyOtpHandler(_normalizer, _security, store, db, clock, NullLogger<VerifyOtpHandler>.Instance);

        var result = await handler.HandleAsync(new VerifyOtpCommand("01012345678", "654321"));

        result.Error.Code.Should().Be("Identity.AccountPendingDeletion");
    }

    private sealed class FakeClock : Malabak.SharedKernel.IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        public DateTime UtcNow { get; } = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }

    private static IdentityDbContext Db() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class CountingSender : IOtpSender
    {
        public int Sent { get; private set; }

        public Task<bool> SendAsync(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, CancellationToken cancellationToken = default)
        {
            Sent++;
            return Task.FromResult(true);
        }
    }
}
