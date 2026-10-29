using System.Net;
using System.Net.Http.Headers;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using FluentAssertions;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Identity.Application.Authorization;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class AuthorizationPolicyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthorizationPolicyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/v1/venue-manager/venues")]
    [InlineData("/api/v1/venue-manager/venues/0192f3a1-0000-7000-8000-000000000001/blocked-slots")]
    [InlineData("/api/v1/me/permissions")]
    public async Task ProtectedEndpoint_Anonymous_Returns401Async(string url)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/v1/admin/sessions")]
    [InlineData("/api/v1/admin/venues/pending-approval")]
    public async Task AdminEndpoint_WithPlayerAudienceToken_Returns401Async(string url)
    {
        var tokenService = _factory.Services.GetRequiredService<ITokenService>();
        var player = User.CreatePlayer(Guid.NewGuid(), "+201001234567", "Player", DateTime.UtcNow);
        player.AddRole(UserRole.Admin); // even a forged role claim doesn't help: the audience is wrong
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenService.GenerateTokens(player).AccessToken);

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RegularEndpoint_WithAdminAudienceToken_Returns401Async()
    {
        var tokenService = _factory.Services.GetRequiredService<ITokenService>();
        var admin = User.CreateStaff(Guid.NewGuid(), "a@x.com", "+201001234568", "hash", "Admin", UserRole.Admin, DateTime.UtcNow);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenService.GenerateAdminTokens(admin, DateTime.UtcNow.AddHours(1)).AccessToken);

        var response = await client.GetAsync("/api/v1/me/permissions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReferenceData_IsPublic_AndCacheableWithEtagAsync()
    {
        var client = _factory.CreateClient();

        var first = await client.GetAsync("/api/v1/reference/governorates");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.CacheControl!.Public.Should().BeTrue();

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reference/governorates");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", first.Headers.ETag!.Tag);
        (await client.SendAsync(conditional)).StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task PermissionHandler_UserHasPermission_SucceedsAsync()
    {
        var userId = Guid.NewGuid();
        var context = await EvaluateAsync(userId, Permissions.VenuesManage, new UserPermissions(userId, ["VenueOwner"], new HashSet<string> { Permissions.VenuesManage }));

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task PermissionHandler_UserLacksPermission_DoesNotSucceedAsync()
    {
        var userId = Guid.NewGuid();
        var context = await EvaluateAsync(userId, Permissions.VenuesManage, new UserPermissions(userId, ["Player"], new HashSet<string> { Permissions.BookingsCreate }));

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task PermissionHandler_UserNoLongerExists_DoesNotSucceedAsync()
    {
        var context = await EvaluateAsync(Guid.NewGuid(), Permissions.VenuesManage, permissions: null);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task PermissionHandler_TokenRoleClaimAlone_IsNotTrustedAsync()
    {
        // The token says Admin, but the database says Player: the database wins.
        var userId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", userId.ToString()), new Claim(ClaimTypes.Role, "Admin")], "test"));
        var requirement = new PermissionRequirement(Permissions.AdminUsersManage);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new PermissionAuthorizationHandler(
            new FakePermissionService(new UserPermissions(userId, ["Player"], new HashSet<string> { Permissions.BookingsCreate })));

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    private static async Task<AuthorizationHandlerContext> EvaluateAsync(Guid userId, string permission, UserPermissions? permissions)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test"));
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await new PermissionAuthorizationHandler(new FakePermissionService(permissions)).HandleAsync(context);
        return context;
    }

    private sealed class FakePermissionService : IUserPermissionService
    {
        private readonly UserPermissions? _permissions;

        public FakePermissionService(UserPermissions? permissions) => _permissions = permissions;

        public Task<UserPermissions?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_permissions);
    }
}
