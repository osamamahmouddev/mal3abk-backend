using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.UnitTests;

public sealed class EmailTests
{
    private const string _secret = "bounce-webhook-secret-0123456789";
    private readonly FixedClock _clock = new();

    [Fact]
    public void Templates_AreBilingual_WithLinkInTextAndHtml()
    {
        var email = EmailTemplates.PasswordReset("https://venue.malabak.app/reset-password?token=abc");

        email.Subject.Should().Contain("إعادة تعيين").And.Contain("Reset your password");
        email.Text.Should().Contain("30").And.Contain("https://venue.malabak.app/reset-password?token=abc");
        email.Html.Should().Contain("dir=\"rtl\"").And.Contain("dir=\"ltr\"");
        email.Html.Should().Contain("href=\"https://venue.malabak.app/reset-password?token=abc\"");
    }

    [Fact]
    public void Templates_HtmlEncodeUserSuppliedValues()
    {
        var email = EmailTemplates.StaffInvitation("<script>alert(1)</script> FC", "Manager", "https://x/accept?token=t");

        email.Html.Should().NotContain("<script>");
        email.Html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void AllTemplates_RenderWithoutPlaceholdersLeft()
    {
        var emails = new[]
        {
            EmailTemplates.EmailVerification("https://l"),
            EmailTemplates.PasswordReset("https://l"),
            EmailTemplates.StaffInvitation("Club", "Receptionist", "https://l"),
            EmailTemplates.NewDeviceSignIn("Pixel 8", _clock.UtcNow)
        };

        emails.Should().OnlyContain(e => !e.Text.Contains('{') && !e.Html.Contains("{0}") && e.Subject.Length > 0);
    }

    [Fact]
    public async Task Bounce_SignedHardBounce_SuppressesAddressByHashAsync()
    {
        await using var db = NewDb();
        var handler = Handler(db, _secret);
        const string body = "{\"email\":\"User@Example.com\",\"type\":\"hard\"}";

        var result = await handler.ApplyAsync(body, Sign(body), "User@Example.com", "hard");

        result.Should().Be(BounceResult.Applied);
        var suppression = await db.EmailSuppressions.SingleAsync();
        suppression.EmailHash.Should().Be(SmtpEmailSender.HashAddress("user@example.com"));
        suppression.EmailHash.Should().NotContain("example");
        suppression.Reason.Should().Be(EmailSuppressionReason.HardBounce);
    }

    [Fact]
    public async Task Bounce_UnsignedOrWithoutSecret_IsRejectedAsync()
    {
        await using var db = NewDb();
        const string body = "{\"email\":\"a@b.com\",\"type\":\"complaint\"}";

        (await Handler(db, _secret).ApplyAsync(body, "deadbeef", "a@b.com", "complaint")).Should().Be(BounceResult.InvalidSignature);
        (await Handler(db, secret: null).ApplyAsync(body, Sign(body), "a@b.com", "complaint")).Should().Be(BounceResult.InvalidSignature,
            "no secret configured means the webhook is disabled");
        (await db.EmailSuppressions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Bounce_SoftBounce_IsIgnored_AndRepeatsAreIdempotentAsync()
    {
        await using var db = NewDb();
        var handler = Handler(db, _secret);
        const string soft = "{\"email\":\"a@b.com\",\"type\":\"soft\"}";
        const string complaint = "{\"email\":\"a@b.com\",\"type\":\"complaint\"}";

        (await handler.ApplyAsync(soft, Sign(soft), "a@b.com", "soft")).Should().Be(BounceResult.Ignored);
        await handler.ApplyAsync(complaint, Sign(complaint), "a@b.com", "complaint");
        await handler.ApplyAsync(complaint, Sign(complaint), "a@b.com", "complaint");

        (await db.EmailSuppressions.CountAsync()).Should().Be(1);
    }

    private EmailBounceHandler Handler(IdentityDbContext db, string? secret)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Email:WebhookSecret"] = secret }).Build();
        return new EmailBounceHandler(db, _clock, config, NullLogger<EmailBounceHandler>.Instance);
    }

    private static string Sign(string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
