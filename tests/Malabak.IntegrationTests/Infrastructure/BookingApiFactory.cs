using System.Net.Http.Headers;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// The real API on the test PostgreSQL and Redis, with every module's migrations applied when the host starts, and a helper
/// that creates real players: permissions are read from the identity database on each request and the device session is
/// checked on each request, so a test token alone is not enough.
/// </summary>
public sealed class BookingApiFactory : MalabakWebApplicationFactory
{
    /// <summary>A real player in the identity database with a real device session and the access token a login would issue.</summary>
    public async Task<(Guid PlayerId, HttpClient Client)> CreatePlayerAsync()
    {
        var playerId = Guid.NewGuid();
        var phone = $"+2010{Random.Shared.Next(10_000_000, 99_999_999)}";
        var player = User.CreatePlayer(playerId, phone, "Booking Test Player", DateTime.UtcNow);

        Guid sessionId;
        using (var scope = Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.Users.Add(player);
            await identity.SaveChangesAsync();
            sessionId = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(player, isAdminSession: false);
        }

        var token = Services.GetRequiredService<ITokenService>().GenerateTokens(player, sessionId).AccessToken;
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (playerId, client);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // The shared test database starts empty: apply every module's real migrations when the host starts.
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }
}
