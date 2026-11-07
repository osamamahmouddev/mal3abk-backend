using FluentAssertions;
using Malabak.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.UnitTests;

public sealed class CloudflareAccessTests
{
    [Theory]
    [InlineData("/hangfire", 403)]
    [InlineData("/hangfire/jobs/enqueued", 403)]
    [InlineData("/swagger/index.html", 403)]
    [InlineData("/api/v1/venues/explore", 200)]
    [InlineData("/health/live", 200)]
    public async Task InternalTools_NeedAnAccessAssertion_WhenAccessIsConfiguredAsync(string path, int expected)
    {
        var options = Options.Create(new CloudflareAccessOptions { TeamDomain = "malabak.cloudflareaccess.com", Audiences = ["aud-tag"] });

        var status = await RunAsync(options, path, assertion: null);

        status.Should().Be(expected);
    }

    [Fact]
    public async Task AForgedAssertion_IsRejectedAsync()
    {
        var options = Options.Create(new CloudflareAccessOptions { TeamDomain = "malabak.cloudflareaccess.com", Audiences = ["aud-tag"] });

        (await RunAsync(options, "/hangfire", "not-a-jwt")).Should().Be(403);
    }

    [Fact]
    public async Task WithoutATeamDomain_TheCheckIsOff_ForLocalDevelopmentAsync()
    {
        (await RunAsync(Options.Create(new CloudflareAccessOptions()), "/hangfire", null)).Should().Be(200);
    }

    private static async Task<int> RunAsync(IOptions<CloudflareAccessOptions> options, string path, string? assertion)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (assertion is not null)
        {
            context.Request.Headers[CloudflareAccessValidator.AssertionHeader] = assertion;
        }

        // No network: the validator only fetches keys after the cheap checks pass, and a malformed token fails before that.
        var validator = new CloudflareAccessValidator(options, new NoHttp(), NullLogger<CloudflareAccessValidator>.Instance);
        var middleware = new CloudflareAccessGuardMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context, options, validator);
        return context.Response.StatusCode;
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FailingHandler());

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("no network in unit tests");
        }
    }
}
