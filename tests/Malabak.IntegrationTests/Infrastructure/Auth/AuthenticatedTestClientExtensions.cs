using System.Net.Http.Headers;
using Malabak.Modules.Identity.Domain;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Malabak.IntegrationTests.Infrastructure.Auth;

public static class AuthenticatedTestClientExtensions
{
    public static HttpClient AsAnonymous(this HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    public static HttpClient AsPlayer(
        this HttpClient client,
        IServiceProvider services,
        Guid? userId = null,
        string? phoneNumber = null)
    {
        return client.WithRole(services, UserRole.Player, userId, phoneNumber);
    }

    public static HttpClient AsVenueOwner(
        this HttpClient client,
        IServiceProvider services,
        Guid? venueId = null,
        Guid? userId = null)
    {
        var claims = new Dictionary<string, string>();
        if (venueId.HasValue)
        {
            claims["venue_id"] = venueId.Value.ToString();
        }

        return client.WithRole(services, UserRole.VenueOwner, userId, customClaims: claims);
    }

    public static HttpClient AsVenueStaff(
        this HttpClient client,
        IServiceProvider services,
        Guid? venueId = null,
        Guid? userId = null)
    {
        var claims = new Dictionary<string, string>();
        if (venueId.HasValue)
        {
            claims["venue_id"] = venueId.Value.ToString();
        }

        return client.WithRole(services, UserRole.VenueStaff, userId, customClaims: claims);
    }

    public static HttpClient AsAdmin(
        this HttpClient client,
        IServiceProvider services,
        Guid? userId = null)
    {
        return client.WithRole(services, UserRole.Admin, userId);
    }

    public static HttpClient AsSupportAgent(
        this HttpClient client,
        IServiceProvider services,
        Guid? userId = null)
    {
        return client.WithRole(services, UserRole.SupportAgent, userId);
    }

    public static HttpClient AsFinance(
        this HttpClient client,
        IServiceProvider services,
        Guid? userId = null)
    {
        return client.WithRole(services, UserRole.Finance, userId);
    }

    public static HttpClient WithRole(
        this HttpClient client,
        IServiceProvider services,
        string role,
        Guid? userId = null,
        string? phoneNumber = null,
        IDictionary<string, string>? customClaims = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(services);

        var token = TestJwtTokenGenerator.GenerateToken(services, role, userId, phoneNumber, customClaims);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static HttpClient WithBearerToken(this HttpClient client, string token)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
