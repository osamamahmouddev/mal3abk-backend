using System.Net.Http.Headers;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Booking.Infrastructure;
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

    /// <summary>A real venue owner with a venue of their own in the Venues database, signed in the way a login would be.</summary>
    public async Task<(Guid OwnerId, Guid VenueId, Guid PitchId, HttpClient Client)> CreateVenueOwnerAsync()
    {
        var ownerId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N");
        var owner = User.CreateVenueOwner(ownerId, $"owner-{unique}@example.test", $"+2011{Random.Shared.Next(10_000_000, 99_999_999)}", "x", "Venue Owner", DateTime.UtcNow);

        Guid sessionId;
        var venueId = Guid.NewGuid();
        var pitchId = Guid.NewGuid();
        using (var scope = Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.Users.Add(owner);
            await identity.SaveChangesAsync();
            sessionId = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(owner, isAdminSession: false);

            var venues = scope.ServiceProvider.GetRequiredService<VenuesDbContext>();
            venues.Venues.Add(Venue.Create(venueId, "Test Club", $"club-{unique}", null, ownerId, "1 Test St", "Cairo", "Maadi", null, null, "+201000000001", null, null, DateTime.UtcNow));
            venues.Pitches.Add(Pitch.Create(pitchId, venueId, "Pitch 1", "ArtificialTurf", 10, null, null, null, DateTime.UtcNow));
            await venues.SaveChangesAsync();
        }

        var token = Services.GetRequiredService<ITokenService>().GenerateTokens(owner, sessionId).AccessToken;
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (ownerId, venueId, pitchId, client);
    }

    /// <summary>A real staff member of the venue with the given role, signed in the way a login would be.</summary>
    public async Task<HttpClient> CreateStaffAsync(Guid venueId, Guid ownerId, VenueStaffRole role)
    {
        var staffId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N");
        var staff = User.CreateStaff(staffId, $"staff-{unique}@example.test", $"+2012{Random.Shared.Next(10_000_000, 99_999_999)}", "x", "Venue Staff", UserRole.VenueStaff, DateTime.UtcNow);

        Guid sessionId;
        using (var scope = Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.Users.Add(staff);
            await identity.SaveChangesAsync();
            sessionId = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(staff, isAdminSession: false);

            var venues = scope.ServiceProvider.GetRequiredService<VenuesDbContext>();
            venues.StaffMembers.Add(VenueStaffMember.Create(venueId, staffId, role, ownerId, DateTime.UtcNow));
            await venues.SaveChangesAsync();
        }

        var token = Services.GetRequiredService<ITokenService>().GenerateTokens(staff, sessionId).AccessToken;
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Saves a booking straight into the Booking database, in the state the test needs.</summary>
    public async Task SaveBookingAsync(Booking booking)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // The shared test database starts empty: apply every module's real migrations when the host starts.
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }
}
