using System.Net;
using System.Text.Json;
using FluentAssertions;
using Malabak.Modules.Identity;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class OtpDeliveryTests
{
    // Shape-valid but fake: only the host is checked, and tests never reach Slack.
    internal const string WebhookUrl = "https://hooks.slack.com/services/T000/B000/test";

    [Theory]
    [InlineData("+201012345678", "+2010•••••678")]
    [InlineData("+201099998888", "+2010•••••888")]
    [InlineData("+2010", "•••••")]
    [InlineData("", "")]
    public void MaskPhoneNumber_KeepsPrefixAndLastThreeDigits(string phoneNumber, string expected)
    {
        SlackOtpSender.MaskPhoneNumber(phoneNumber).Should().Be(expected);
    }

    [Fact]
    public void BuildMessage_ContainsCodePurposeAndMaskedPhone_NotTheFullPhone()
    {
        var message = SlackOtpSender.BuildMessage("+201012345678", "482913", OtpPurpose.Login, TimeSpan.FromMinutes(5), "Staging");

        message.Should().Contain("482913");
        message.Should().Contain("+2010•••••678");
        message.Should().Contain("تسجيل الدخول");
        message.Should().Contain("5 دقيقة");
        message.Should().Contain("Staging");
        message.Should().NotContain("+201012345678");
    }

    [Fact]
    public async Task SendAsync_PostsTheMessageToTheWebhookAsync()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var sender = CreateSender(handler);

        var sent = await sender.SendAsync("+201012345678", "482913", OtpPurpose.ChangePhoneNew, TimeSpan.FromMinutes(15));

        sent.Should().BeTrue();
        handler.RequestUri.Should().Be(new Uri(WebhookUrl));
        handler.Method.Should().Be(HttpMethod.Post);
        using var body = JsonDocument.Parse(handler.Body!);
        var text = body.RootElement.GetProperty("text").GetString();
        text.Should().Contain("482913").And.Contain("+2010•••••678").And.Contain("الرقم الجديد");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SendAsync_WhenSlackRejects_ReturnsFalseAsync(HttpStatusCode status)
    {
        var sender = CreateSender(new StubHandler(status));

        var sent = await sender.SendAsync("+201012345678", "482913", OtpPurpose.Login, TimeSpan.FromMinutes(5));

        sent.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WhenSlackIsUnreachable_ReturnsFalseAsync()
    {
        var sender = CreateSender(new StubHandler(new HttpRequestException("Connection refused (hooks.slack.com:443)")));

        var sent = await sender.SendAsync("+201012345678", "482913", OtpPurpose.Login, TimeSpan.FromMinutes(5));

        sent.Should().BeFalse();
    }

    [Fact]
    public async Task NoProvider_RegistersASenderThatFailsClosedAsync()
    {
        using var provider = BuildServices(Environments.Development, new() { ["Identity:OtpDelivery:Provider"] = "None" });

        var sender = provider.GetRequiredService<IOtpSender>();

        sender.Should().BeOfType<UnavailableOtpSender>();
        (await sender.SendAsync("+201012345678", "482913", OtpPurpose.Login, TimeSpan.FromMinutes(5))).Should().BeFalse();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void SlackProvider_OutsideProduction_IsRegistered(string environment)
    {
        using var provider = BuildServices(environment, SlackSettings(WebhookUrl));

        provider.GetRequiredService<IOptions<OtpDeliveryOptions>>().Value.Provider.Should().Be(OtpDeliveryProvider.Slack);
        provider.GetRequiredService<IOtpSender>().Should().BeOfType<SlackOtpSender>();
    }

    [Fact]
    public void SlackProvider_InProduction_IsRefused()
    {
        using var provider = BuildServices(Environments.Production, SlackSettings(WebhookUrl));

        var act = () => provider.GetRequiredService<IOptions<OtpDeliveryOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*not allowed in Production*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://hooks.slack.com/services/T000/B000/test")]
    [InlineData("https://example.com/services/T000/B000/test")]
    [InlineData("not a url")]
    public void SlackProvider_WithoutAValidWebhookUrl_IsRefused(string webhookUrl)
    {
        using var provider = BuildServices(Environments.Development, SlackSettings(webhookUrl));

        var act = () => provider.GetRequiredService<IOptions<OtpDeliveryOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*hooks.slack.com*");
    }

    private static SlackOtpSender CreateSender(StubHandler handler) => new(
        new SlackWebhookClient(
            new HttpClient(handler),
            Options.Create(new OtpDeliveryOptions { Provider = OtpDeliveryProvider.Slack, Slack = { WebhookUrl = WebhookUrl } }),
            NullLogger<SlackWebhookClient>.Instance),
        new TestHostEnvironment(Environments.Development));

    internal static Dictionary<string, string?> SlackSettings(string webhookUrl) => new()
    {
        ["Identity:OtpDelivery:Provider"] = "Slack",
        ["Identity:OtpDelivery:Slack:WebhookUrl"] = webhookUrl,
    };

    internal static ServiceProvider BuildServices(string environment, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddIdentityModule(configuration);
        return services.BuildServiceProvider();
    }

    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly Exception? _exception;

        public StubHandler(HttpStatusCode status) => _status = status;

        public StubHandler(Exception exception) => _exception = exception;

        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            RequestUri = request.RequestUri;
            Method = request.Method;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status);
        }
    }

    internal sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Malabak.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
