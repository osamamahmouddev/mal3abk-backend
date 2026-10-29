using System.Net;
using System.Text.Json;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class SecurityAlertDeliveryTests
{
    private const string _oldPhone = "+201011112222";
    private const string _newPhone = "+201099998888";
    private const string _recoveryToken = "live-recovery-token-Qx7vT2";
    private const string _recoveryLink = "https://malabak.app/recover-phone?token=" + _recoveryToken;

    private static readonly SecurityAlert _phoneChanged = SecurityAlert.PhoneChanged(_newPhone, _recoveryLink);

    [Fact]
    public void ToString_NamesOnlyTheKind()
    {
        var text = _phoneChanged.ToString();

        text.Should().Contain(nameof(SecurityAlertKind.PhoneChanged));
        text.Should().NotContain(_newPhone).And.NotContain(_recoveryToken);
    }

    [Fact]
    public void BuildMessage_PhoneChanged_MasksBothNumbersAndIncludesTheRecoveryLink()
    {
        var message = SlackSmsAlertService.BuildMessage(_oldPhone, _phoneChanged, "Staging");

        message.Should().Contain("+2010•••••222").And.Contain("+2010•••••888").And.Contain(_recoveryLink).And.Contain("Staging");
        message.Should().NotContain(_oldPhone).And.NotContain(_newPhone);
    }

    [Fact]
    public void BuildMessage_PhoneRestored_MasksTheNumber()
    {
        var message = SlackSmsAlertService.BuildMessage(_oldPhone, SecurityAlert.PhoneRestored(), "Development");

        message.Should().Contain("+2010•••••222").And.Contain("استرجاع");
        message.Should().NotContain(_oldPhone);
    }

    [Fact]
    public async Task NoProvider_DropsTheAlertAndLogsOnlyItsKindAsync()
    {
        var logger = new CapturingLogger<UnavailableSmsAlertService>();
        var service = new UnavailableSmsAlertService(logger);

        await service.SendSecurityAlertAsync(_oldPhone, _phoneChanged);

        logger.Entries.Should().ContainSingle().Which.Should().Contain(nameof(SecurityAlertKind.PhoneChanged));
        AssertNoSecrets(logger.AllText);
    }

    [Fact]
    public void NoProvider_RegistersTheDroppingService()
    {
        using var provider = OtpDeliveryTests.BuildServices(Environments.Development, new() { ["Identity:OtpDelivery:Provider"] = "None" });

        provider.GetRequiredService<ISmsAlertService>().Should().BeOfType<UnavailableSmsAlertService>();
    }

    [Fact]
    public void SlackProvider_OutsideProduction_RegistersTheSlackService()
    {
        using var provider = OtpDeliveryTests.BuildServices(Environments.Staging, OtpDeliveryTests.SlackSettings(OtpDeliveryTests.WebhookUrl));

        provider.GetRequiredService<ISmsAlertService>().Should().BeOfType<SlackSmsAlertService>();
    }

    [Fact]
    public async Task SlackProvider_PostsTheAlertAndLogsNothingAsync()
    {
        var handler = new OtpDeliveryTests.StubHandler(HttpStatusCode.OK);
        var logger = new CapturingLogger<SlackWebhookClient>();

        await CreateSlackService(handler, logger).SendSecurityAlertAsync(_oldPhone, _phoneChanged);

        handler.RequestUri.Should().Be(new Uri(OtpDeliveryTests.WebhookUrl));
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("text").GetString().Should().Contain(_recoveryLink).And.NotContain(_oldPhone);
        logger.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SlackProvider_WhenSlackRejects_LogsTheKindButNoSecretsAsync(HttpStatusCode status)
    {
        var logger = new CapturingLogger<SlackWebhookClient>();

        await CreateSlackService(new OtpDeliveryTests.StubHandler(status), logger).SendSecurityAlertAsync(_oldPhone, _phoneChanged);

        logger.Entries.Should().ContainSingle().Which.Should().Contain(nameof(SecurityAlertKind.PhoneChanged));
        AssertNoSecrets(logger.AllText);
    }

    [Fact]
    public async Task SlackProvider_WhenSlackIsUnreachable_LogsTheKindButNoSecretsAsync()
    {
        var logger = new CapturingLogger<SlackWebhookClient>();
        var handler = new OtpDeliveryTests.StubHandler(new HttpRequestException("Connection refused (hooks.slack.com:443)"));

        await CreateSlackService(handler, logger).SendSecurityAlertAsync(_oldPhone, _phoneChanged);

        logger.Entries.Should().ContainSingle().Which.Should().Contain(nameof(SecurityAlertKind.PhoneChanged));
        AssertNoSecrets(logger.AllText);
    }

    private static void AssertNoSecrets(string logText)
    {
        logText.Should().NotContain(_oldPhone)
            .And.NotContain(_newPhone)
            .And.NotContain(_recoveryToken)
            .And.NotContain(OtpDeliveryTests.WebhookUrl);
    }

    private static SlackSmsAlertService CreateSlackService(OtpDeliveryTests.StubHandler handler, CapturingLogger<SlackWebhookClient> logger) => new(
        new SlackWebhookClient(
            new HttpClient(handler),
            Options.Create(new OtpDeliveryOptions { Provider = OtpDeliveryProvider.Slack, Slack = { WebhookUrl = OtpDeliveryTests.WebhookUrl } }),
            logger),
        new OtpDeliveryTests.TestHostEnvironment(Environments.Development));
}
