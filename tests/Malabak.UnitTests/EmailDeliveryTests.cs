using System.Net;
using System.Text.Json;
using FluentAssertions;
using Malabak.Modules.Identity;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class EmailDeliveryTests
{
    private const string _emailWebhookUrl = "https://hooks.slack.com/services/T000/B000/email";

    [Theory]
    [InlineData("owner@gmail.com", "o••••@gmail.com")]
    [InlineData("a@b.co", "a•@b.co")]
    [InlineData("no-at-sign", "••••••••••")]
    public void MaskEmail_KeepsFirstCharacterAndDomain(string address, string expected)
    {
        SlackEmailSender.MaskEmail(address).Should().Be(expected);
    }

    [Fact]
    public async Task SendAsync_PostsSubjectBodyAndMaskedRecipientToTheEmailWebhookAsync()
    {
        var handler = new OtpDeliveryTests.StubHandler(HttpStatusCode.OK);
        var sender = CreateSender(handler, _emailWebhookUrl);

        await sender.SendAsync("owner@gmail.com", EmailTemplates.EmailVerification("https://venue.malabak.app/verify-email?token=abc"));

        handler.RequestUri.Should().Be(new Uri(_emailWebhookUrl));
        using var body = JsonDocument.Parse(handler.Body!);
        var text = body.RootElement.GetProperty("text").GetString();
        text.Should().Contain("Verify your email").And.Contain("verify-email?token=abc").And.Contain("o••••@gmail.com");
        text.Should().NotContain("owner@gmail.com");
    }

    [Fact]
    public async Task SendAsync_WithoutAnEmailWebhook_UsesTheOtpWebhookAsync()
    {
        var handler = new OtpDeliveryTests.StubHandler(HttpStatusCode.OK);
        var sender = CreateSender(handler, emailWebhookUrl: null);

        await sender.SendAsync("owner@gmail.com", "subject", "body");

        handler.RequestUri.Should().Be(new Uri(OtpDeliveryTests.WebhookUrl));
    }

    [Fact]
    public async Task SendAsync_WhenSlackRejects_DoesNotThrowAsync()
    {
        var sender = CreateSender(new OtpDeliveryTests.StubHandler(HttpStatusCode.InternalServerError), _emailWebhookUrl);

        var act = () => sender.SendAsync("owner@gmail.com", "subject", "body");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void DefaultProvider_IsSmtp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new OtpDeliveryTests.TestHostEnvironment(Environments.Development));
        services.AddIdentityModule(new ConfigurationBuilder().Build());

        // Not resolved: the SMTP sender needs the host's IConfiguration, which this minimal test host lacks.
        services.Single(d => d.ServiceType == typeof(IEmailSender)).ImplementationType.Should().Be(typeof(SmtpEmailSender));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void SlackProvider_OutsideProduction_UsesTheOtpWebhookWhenNoneIsSet(string environment)
    {
        var settings = OtpDeliveryTests.SlackSettings(OtpDeliveryTests.WebhookUrl);
        settings["Identity:EmailDelivery:Provider"] = "Slack";
        using var provider = OtpDeliveryTests.BuildServices(environment, settings);

        provider.GetRequiredService<IOptions<EmailDeliveryOptions>>().Value.Provider.Should().Be(EmailDeliveryProvider.Slack);
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<SlackEmailSender>();
    }

    [Fact]
    public void SlackProvider_InProduction_IsRefused()
    {
        using var provider = OtpDeliveryTests.BuildServices(Environments.Production, new()
        {
            ["Identity:EmailDelivery:Provider"] = "Slack",
            ["Identity:EmailDelivery:Slack:WebhookUrl"] = _emailWebhookUrl,
        });

        var act = () => provider.GetRequiredService<IOptions<EmailDeliveryOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*not allowed in Production*");
    }

    [Fact]
    public void SlackProvider_WithoutAnyWebhook_IsRefused()
    {
        using var provider = OtpDeliveryTests.BuildServices(Environments.Development, new() { ["Identity:EmailDelivery:Provider"] = "Slack" });

        var act = () => provider.GetRequiredService<IOptions<EmailDeliveryOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*hooks.slack.com*");
    }

    private static SlackEmailSender CreateSender(OtpDeliveryTests.StubHandler handler, string? emailWebhookUrl) => new(
        new SlackWebhookClient(
            new HttpClient(handler),
            Options.Create(new OtpDeliveryOptions { Provider = OtpDeliveryProvider.Slack, Slack = { WebhookUrl = OtpDeliveryTests.WebhookUrl } }),
            NullLogger<SlackWebhookClient>.Instance),
        Options.Create(new EmailDeliveryOptions { Provider = EmailDeliveryProvider.Slack, Slack = { WebhookUrl = emailWebhookUrl } }),
        new OtpDeliveryTests.TestHostEnvironment(Environments.Development));
}
