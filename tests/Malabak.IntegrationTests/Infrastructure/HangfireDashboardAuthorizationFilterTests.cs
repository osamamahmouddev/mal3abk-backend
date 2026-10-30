using System.Security.Claims;
using FluentAssertions;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.Storage;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class HangfireDashboardAuthorizationFilterTests
{
    [Fact]
    public void Authorize_WhenInDevelopmentAndBypassEnabled_ShouldReturnTrue()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions
        {
            BypassDashboardAuthInDevelopment = true
        });
        var env = new FakeEnvironment { EnvironmentName = Environments.Development };
        var filter = new HangfireDashboardAuthorizationFilter(
            options,
            env,
            NullLogger<HangfireDashboardAuthorizationFilter>.Instance);

        var context = CreateDashboardContext(new DefaultHttpContext());

        // Act
        var authorized = filter.Authorize(context);

        // Assert
        authorized.Should().BeTrue();
    }

    [Fact]
    public void Authorize_WhenUnauthenticated_ShouldReturnFalse()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions
        {
            BypassDashboardAuthInDevelopment = false
        });
        var env = new FakeEnvironment { EnvironmentName = Environments.Production };
        var filter = new HangfireDashboardAuthorizationFilter(
            options,
            env,
            NullLogger<HangfireDashboardAuthorizationFilter>.Instance);

        var httpContext = new DefaultHttpContext();
        var context = CreateDashboardContext(httpContext);

        // Act
        var authorized = filter.Authorize(context);

        // Assert
        authorized.Should().BeFalse();
    }

    [Fact]
    public void Authorize_WhenAuthenticatedAsNonAdmin_ShouldReturnFalse()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions
        {
            BypassDashboardAuthInDevelopment = false
        });
        var env = new FakeEnvironment { EnvironmentName = Environments.Production };
        var filter = new HangfireDashboardAuthorizationFilter(
            options,
            env,
            NullLogger<HangfireDashboardAuthorizationFilter>.Instance);

        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "player1"), new Claim(ClaimTypes.Role, "Player")],
            "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);
        var context = CreateDashboardContext(httpContext);

        // Act
        var authorized = filter.Authorize(context);

        // Assert
        authorized.Should().BeFalse();
    }

    [Fact]
    public void Authorize_WhenAuthenticatedAsAdmin_ShouldReturnTrue()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions
        {
            BypassDashboardAuthInDevelopment = false
        });
        var env = new FakeEnvironment { EnvironmentName = Environments.Production };
        var filter = new HangfireDashboardAuthorizationFilter(
            options,
            env,
            NullLogger<HangfireDashboardAuthorizationFilter>.Instance);

        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "superadmin"), new Claim(ClaimTypes.Role, "Admin")],
            "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);
        var context = CreateDashboardContext(httpContext);

        // Act
        var authorized = filter.Authorize(context);

        // Assert
        authorized.Should().BeTrue();
    }

    [Fact]
    public void Authorize_WhenCloudflareAccessConfiguredAndHeaderMissing_ShouldReturnFalse()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions
        {
            CloudflareAccessTeamDomain = "https://malabak.cloudflareaccess.com",
            BypassDashboardAuthInDevelopment = false
        });
        var env = new FakeEnvironment { EnvironmentName = Environments.Production };
        var filter = new HangfireDashboardAuthorizationFilter(
            options,
            env,
            NullLogger<HangfireDashboardAuthorizationFilter>.Instance);

        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);
        var context = CreateDashboardContext(httpContext);

        // Act
        var authorized = filter.Authorize(context);

        // Assert
        authorized.Should().BeFalse();
    }

    [Fact]
    public void Authorize_WhenCloudflareAccessConfiguredAndHeaderPresentAndAdmin_ShouldReturnTrue()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions
        {
            CloudflareAccessTeamDomain = "https://malabak.cloudflareaccess.com",
            BypassDashboardAuthInDevelopment = false
        });
        var env = new FakeEnvironment { EnvironmentName = Environments.Production };
        var filter = new HangfireDashboardAuthorizationFilter(
            options,
            env,
            NullLogger<HangfireDashboardAuthorizationFilter>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Cf-Access-Authenticated-User-Email"] = "admin@malabak.app";
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);
        var context = CreateDashboardContext(httpContext);

        // Act
        var authorized = filter.Authorize(context);

        // Assert
        authorized.Should().BeTrue();
    }

    private static AspNetCoreDashboardContext CreateDashboardContext(HttpContext httpContext)
    {
        httpContext.RequestServices = new FakeServiceProvider();
        return new AspNetCoreDashboardContext(new FakeJobStorage(), new DashboardOptions(), httpContext);
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FakeJobStorage : JobStorage
    {
        public override IStorageConnection GetConnection() => null!;
        public override IMonitoringApi GetMonitoringApi() => null!;
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Malabak.Api";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
