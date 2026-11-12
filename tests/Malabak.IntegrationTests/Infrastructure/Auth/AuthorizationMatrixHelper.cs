using System.Net;
using System.Text;
using FluentAssertions;
using Malabak.Modules.Identity.Domain;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Malabak.IntegrationTests.Infrastructure.Auth;

public sealed class AuthorizationMatrixHelper
{
    private const string _anonymousRoleKey = "__Anonymous__";
    private readonly HttpMethod _method;
    private readonly string _path;
    private readonly Func<HttpContent>? _contentFactory;
    private readonly Dictionary<string, HttpStatusCode> _expectedStatuses = [];

    private AuthorizationMatrixHelper(HttpMethod method, string path, Func<HttpContent>? contentFactory = null)
    {
        _method = method;
        _path = path;
        _contentFactory = contentFactory;
    }

    public static AuthorizationMatrixHelper ForEndpoint(
        HttpMethod method,
        string path,
        Func<HttpContent>? contentFactory = null)
    {
        return new AuthorizationMatrixHelper(method, path, contentFactory);
    }

    public static AuthorizationMatrixHelper ForJsonEndpoint(
        HttpMethod method,
        string path,
        string jsonBody)
    {
        return new AuthorizationMatrixHelper(
            method,
            path,
            () => new StringContent(jsonBody, Encoding.UTF8, "application/json"));
    }

    public AuthorizationMatrixHelper WithAnonymousExpecting(HttpStatusCode expected)
    {
        _expectedStatuses[_anonymousRoleKey] = expected;
        return this;
    }

    public AuthorizationMatrixHelper WithRoleExpecting(string role, HttpStatusCode expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _expectedStatuses[role] = expected;
        return this;
    }

    public AuthorizationMatrixHelper WithPlayerExpecting(HttpStatusCode expected) =>
        WithRoleExpecting(UserRole.Player, expected);

    public AuthorizationMatrixHelper WithVenueOwnerExpecting(HttpStatusCode expected) =>
        WithRoleExpecting(UserRole.VenueOwner, expected);

    public AuthorizationMatrixHelper WithVenueStaffExpecting(HttpStatusCode expected) =>
        WithRoleExpecting(UserRole.VenueStaff, expected);

    public AuthorizationMatrixHelper WithAdminExpecting(HttpStatusCode expected) =>
        WithRoleExpecting(UserRole.Admin, expected);

    public AuthorizationMatrixHelper WithSupportAgentExpecting(HttpStatusCode expected) =>
        WithRoleExpecting(UserRole.SupportAgent, expected);

    public AuthorizationMatrixHelper WithFinanceExpecting(HttpStatusCode expected) =>
        WithRoleExpecting(UserRole.Finance, expected);

    public async Task ExecuteAsync(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        foreach (var (roleKey, expectedStatus) in _expectedStatuses)
        {
            using var client = factory.CreateClient();

            var isAnonymous = roleKey == _anonymousRoleKey;
            if (isAnonymous)
            {
                client.AsAnonymous();
            }
            else
            {
                client.WithRole(factory.Services, roleKey);
            }

            using var request = new HttpRequestMessage(_method, _path);
            if (_contentFactory != null)
            {
                request.Content = _contentFactory();
            }

            var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            var roleDescription = isAnonymous ? "Anonymous" : roleKey;
            response.StatusCode.Should().Be(
                expectedStatus,
                $"role '{roleDescription}' calling {_method} {_path} should receive {expectedStatus}, but got {response.StatusCode}. Response: {responseBody}");
        }
    }
}
