using FluentAssertions;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>A rate-limited code request says when the next one is allowed, so the app can count down.</summary>
public sealed class SendOtpRetryAfterTests
{
    [Fact]
    public async Task SendOtp_WhenRateLimited_ReturnsTimeUntilTheNextCodeAsync()
    {
        var securityService = new OtpSecurityService(Options.Create(new OtpSecurityOptions { HmacSecret = TestSecrets.OtpHmacSecret }));
        var handler = new SendOtpHandler(
            new PhoneNumberNormalizer(),
            TestSecrets.CreateDevelopmentTurnstileValidator(),
            securityService,
            new InMemoryOtpStore(),
            new AcceptingOtpSender(),
            NullLogger<SendOtpHandler>.Instance);
        var command = new SendOtpCommand("01012345678", "test-turnstile-token");

        for (var i = 0; i < 3; i++)
        {
            (await handler.HandleAsync(command)).IsSuccess.Should().BeTrue();
        }

        var limited = await handler.HandleAsync(command);

        limited.IsFailure.Should().BeTrue();
        limited.Error.Code.Should().Be("Identity.RateLimitExceeded");
        limited.Error.RetryAfter.Should().NotBeNull();
        limited.Error.RetryAfter!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30));
    }

    private sealed class AcceptingOtpSender : IOtpSender
    {
        public Task<bool> SendAsync(string phoneNumber, string code, OtpPurpose purpose, TimeSpan validity, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
