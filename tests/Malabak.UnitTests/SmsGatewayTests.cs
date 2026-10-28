using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Identity.Infrastructure.Sms;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class SmsGatewayTests
{
    private const string _egyptPhone = "+201001234567";
    private readonly FixedClock _clock = new();

    private SmsOptions Options(params string[] providers) => new()
    {
        Providers = providers.ToList(),
        DailySpendCapEgp = 10m,
        MonthlySpendCapEgp = 100m,
        DefaultCostPerMessageEgp = 1m
    };

    private SmsGateway Gateway(IdentityDbContext db, SmsOptions options, params ISmsProvider[] providers) =>
        new(db, providers, Microsoft.Extensions.Options.Options.Create(options), _clock, NullLogger<SmsGateway>.Instance);

    [Fact]
    public async Task Send_ToNonEgyptianNumber_IsBlocked_BeforeAnyProviderIsCalledAsync()
    {
        await using var db = NewDb();
        var provider = new FakeProvider("p1");

        var outcome = await Gateway(db, Options("p1"), provider).SendAsync("+447700900123", "hi", SmsTemplates.SecurityAlert);

        outcome.Should().Be(SmsSendOutcome.CountryNotAllowed);
        provider.Sent.Should().BeEmpty();
        (await db.SmsDispatches.SingleAsync()).Status.Should().Be(SmsDeliveryStatus.Blocked);
    }

    [Fact]
    public async Task Send_FailsOverToNextProvider_InConfiguredOrderAsync()
    {
        await using var db = NewDb();
        var primary = new FakeProvider("primary") { Fail = true };
        var backup = new FakeProvider("backup");

        var outcome = await Gateway(db, Options("primary", "backup"), backup, primary).SendOtpAsync(_egyptPhone, "123456");

        outcome.Should().Be(SmsSendOutcome.Sent);
        primary.Sent.Should().ContainSingle();
        backup.Sent.Should().ContainSingle();
        (await db.SmsDispatches.SingleAsync()).Provider.Should().Be("backup");
    }

    [Fact]
    public async Task Send_AllProvidersFail_ReportsFailureAsync()
    {
        await using var db = NewDb();

        var outcome = await Gateway(db, Options("p1"), new FakeProvider("p1") { Fail = true }).SendOtpAsync(_egyptPhone, "123456");

        outcome.Should().Be(SmsSendOutcome.AllProvidersFailed);
    }

    [Fact]
    public async Task UnlistedProvider_IsNeverUsedAsync()
    {
        await using var db = NewDb();
        var rogue = new FakeProvider("rogue");

        var outcome = await Gateway(db, Options("listed"), rogue).SendOtpAsync(_egyptPhone, "123456");

        outcome.Should().Be(SmsSendOutcome.AllProvidersFailed);
        rogue.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task DailySpendCap_BlocksFurtherSendsAsync()
    {
        await using var db = NewDb();
        var provider = new FakeProvider("p1");
        var gateway = Gateway(db, Options("p1"), provider);

        for (var i = 0; i < 10; i++)
        {
            (await gateway.SendOtpAsync(_egyptPhone, "123456")).Should().Be(SmsSendOutcome.Sent);
        }

        (await gateway.SendOtpAsync(_egyptPhone, "123456")).Should().Be(SmsSendOutcome.SpendCapReached);
        provider.Sent.Should().HaveCount(10);
    }

    [Fact]
    public async Task Dispatch_StoresNoBodyAndNoFullNumberAsync()
    {
        await using var db = NewDb();

        await Gateway(db, Options("p1"), new FakeProvider("p1")).SendOtpAsync(_egyptPhone, "987654");

        var dispatch = await db.SmsDispatches.SingleAsync();
        dispatch.MaskedPhone.Should().Be("+20100****567");
        dispatch.MaskedPhone.Should().NotContain("1234");
        dispatch.Template.Should().Be(SmsTemplates.Otp);
        typeof(SmsDispatch).GetProperties().Select(p => p.Name).Should().NotContain("Body");
    }

    [Fact]
    public async Task WhatsAppChannel_SkipsProvidersWithoutWhatsAppAsync()
    {
        await using var db = NewDb();
        var smsOnly = new FakeProvider("sms-only") { WhatsApp = false };
        var options = Options("sms-only");
        options.OtpChannel = SmsChannel.WhatsApp;

        (await Gateway(db, options, smsOnly).SendOtpAsync(_egyptPhone, "123456")).Should().Be(SmsSendOutcome.AllProvidersFailed);
        smsOnly.Sent.Should().BeEmpty();
    }

    [Fact]
    public void TwilioSignature_ValidatesExactUrlAndParams()
    {
        const string token = "auth-token";
        const string url = "https://api.malabak.app/api/v1/webhooks/sms/twilio/primary";
        var form = new Dictionary<string, string> { ["MessageSid"] = "SM1", ["MessageStatus"] = "delivered" };
        var signature = Sign(token, url + "MessageSidSM1MessageStatusdelivered");

        TwilioSmsProvider.IsValidSignature(token, url, form, signature).Should().BeTrue();
        TwilioSmsProvider.IsValidSignature(token, url + "x", form, signature).Should().BeFalse();
        TwilioSmsProvider.IsValidSignature("other-token", url, form, signature).Should().BeFalse();
        TwilioSmsProvider.IsValidSignature(token, url, form, "not-base64!").Should().BeFalse();
        TwilioSmsProvider.IsValidSignature(token, url, form, null).Should().BeFalse();
    }

    [Fact]
    public async Task DeliveryReceipt_SignedRequest_UpdatesStatus_UnsignedIsRejectedAsync()
    {
        await using var db = NewDb();
        const string url = "https://api.malabak.app/hook";
        var dispatch = SmsDispatch.Create("+20100****567", "hash", SmsTemplates.Otp, "Sms", _clock.UtcNow);
        dispatch.MarkSent("primary", "SM42", 0.5m, _clock.UtcNow);
        db.SmsDispatches.Add(dispatch);
        await db.SaveChangesAsync();

        var options = new SmsOptions();
        options.Twilio["primary"] = new TwilioAccountOptions { AuthToken = "tok", StatusCallbackUrl = url };
        var receipts = new SmsDeliveryReceipts(db, Microsoft.Extensions.Options.Options.Create(options), _clock, NullLogger<SmsDeliveryReceipts>.Instance);
        var form = new Dictionary<string, string> { ["MessageSid"] = "SM42", ["MessageStatus"] = "delivered" };

        (await receipts.ApplyTwilioAsync("primary", form, "forged")).Should().Be(DeliveryReceiptResult.InvalidSignature);
        (await receipts.ApplyTwilioAsync("unknown", form, "x")).Should().Be(DeliveryReceiptResult.UnknownAccount);

        var signed = await receipts.ApplyTwilioAsync("primary", form, Sign("tok", url + "MessageSidSM42MessageStatusdelivered"));
        signed.Should().Be(DeliveryReceiptResult.Applied);
        (await db.SmsDispatches.SingleAsync()).Status.Should().Be(SmsDeliveryStatus.Delivered);
    }

    private static string Sign(string token, string data)
    {
#pragma warning disable CA5350
        return Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(data)));
#pragma warning restore CA5350
    }

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeProvider(string name) : ISmsProvider
    {
        public bool Fail { get; set; }
        public bool WhatsApp { get; set; } = true;
        public List<SmsMessage> Sent { get; } = [];
        public string Name => name;

        public bool Supports(SmsChannel channel) => channel == SmsChannel.Sms || WhatsApp;

        public Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(Fail ? SmsSendResult.Failed("down") : new SmsSendResult(true, "id-" + Sent.Count, 1m, null));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        public DateTime CairoNow => UtcNow.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
