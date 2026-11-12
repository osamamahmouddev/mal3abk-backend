using System.Net;
using FluentAssertions;
using Malabak.Modules.Venues.Domain;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class AuthorizationMatrixSecurityTests : IClassFixture<BookingApiFactory>
{
    private readonly BookingApiFactory _factory;

    public AuthorizationMatrixSecurityTests(BookingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublicEndpoints_AreAccessibleByAllRolesAndAnonymousAsync()
    {
        // Arrange: Prepare clients for all 7 actors
        var anonymousClient = _factory.CreateClient();
        var (_, playerClient) = await _factory.CreatePlayerAsync();
        var (_, venueId, _, ownerClient) = await _factory.CreateVenueOwnerAsync();
        var staffClient = await _factory.CreateStaffAsync(venueId, Guid.NewGuid(), VenueStaffRole.Receptionist);
        var (_, adminClient) = await _factory.CreateAdminAsync();
        var (_, supportClient) = await _factory.CreateSupportAgentAsync();
        var (_, financeClient) = await _factory.CreateFinanceAsync();

        var clients = new (string Role, HttpClient Client)[]
        {
            ("Anonymous", anonymousClient),
            ("Player", playerClient),
            ("VenueOwner", ownerClient),
            ("VenueStaff", staffClient),
            ("Admin", adminClient),
            ("SupportAgent", supportClient),
            ("Finance", financeClient)
        };

        var publicPaths = new[]
        {
            "/api/v1/ping",
            "/api/v1/reference/governorates",
            "/api/v1/reference/facilities",
            "/api/v1/platform/status",
            "/.well-known/security.txt"
        };

        foreach (var path in publicPaths)
        {
            foreach (var (role, client) in clients)
            {
                var response = await client.GetAsync(path);
                response.StatusCode.Should().Be(
                    HttpStatusCode.OK,
                    $"endpoint '{path}' is public and must return 200 OK for {role}");
            }
        }
    }

    [Fact]
    public async Task PlayerProtectedEndpoints_DenyAnonymousAndPrivilegeSeparatedRolesAsync()
    {
        // Arrange
        var anonymousClient = _factory.CreateClient();
        var (_, playerClient) = await _factory.CreatePlayerAsync();
        var (_, adminClient) = await _factory.CreateAdminAsync();

        // 1. /me/permissions uses AnySessionPolicy:
        // Anonymous -> 401 Unauthorized
        var anonResponse = await anonymousClient.GetAsync("/api/v1/me/permissions");
        anonResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Player -> 200 OK
        var playerResponse = await playerClient.GetAsync("/api/v1/me/permissions");
        playerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Admin -> 200 OK (AnySessionPolicy explicitly permits admin sessions to inspect permissions)
        var adminPermResponse = await adminClient.GetAsync("/api/v1/me/permissions");
        adminPermResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Player-only endpoints with default scheme (bookings.create):
        // Admin token (staff audience) -> 401 Unauthorized due to audience isolation (SECURITY.md §3.9)
        var adminBookingResponse = await adminClient.PostAsync(
            "/api/v1/bookings/",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        adminBookingResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VenueManagerEndpoints_EnforceVenuePermissionsAsync()
    {
        // Arrange
        var anonymousClient = _factory.CreateClient();
        var (_, playerClient) = await _factory.CreatePlayerAsync();
        var (_, _, _, ownerClient) = await _factory.CreateVenueOwnerAsync();
        var (_, supportClient) = await _factory.CreateSupportAgentAsync();
        var (_, financeClient) = await _factory.CreateFinanceAsync();

        const string endpoint = "/api/v1/venue-manager/venues";

        // Anonymous -> 401
        (await anonymousClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Player (lacks venues.manage) -> 403 Forbidden
        (await playerClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // SupportAgent -> 401 Unauthorized (Staff audience is rejected on venue manager endpoints configured for api audience)
        var supportStatus = (await supportClient.GetAsync(endpoint)).StatusCode;
        supportStatus.Should().Be(HttpStatusCode.Unauthorized, "Staff audience is rejected on venue manager endpoints configured for api audience");

        // Finance -> 401 Unauthorized (Staff audience is rejected on venue manager endpoints configured for api audience)
        var financeStatus = (await financeClient.GetAsync(endpoint)).StatusCode;
        financeStatus.Should().Be(HttpStatusCode.Unauthorized, "Staff audience is rejected on venue manager endpoints configured for api audience");

        // VenueOwner (has venues.manage) -> 200 OK
        (await ownerClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminEndpoints_EnforceAdminSchemeAndStaffPermissionsAsync()
    {
        // Arrange
        var anonymousClient = _factory.CreateClient();
        var (_, playerClient) = await _factory.CreatePlayerAsync();
        var (_, _, _, ownerClient) = await _factory.CreateVenueOwnerAsync();
        var (_, adminClient) = await _factory.CreateAdminAsync();

        const string endpoint = "/api/v1/admin/dashboard";

        // Anonymous -> 401
        (await anonymousClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Player (wrong audience / lacks admin scheme) -> 401
        (await playerClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // VenueOwner (wrong audience / lacks admin scheme) -> 401
        (await ownerClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Admin -> 200 OK
        (await adminClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SupportEndpoints_AllowSupportAgentAndAdmin_RejectPlayerAndVenueOwnerAsync()
    {
        // Arrange
        var anonymousClient = _factory.CreateClient();
        var (_, playerClient) = await _factory.CreatePlayerAsync();
        var (_, _, _, ownerClient) = await _factory.CreateVenueOwnerAsync();
        var (_, supportClient) = await _factory.CreateSupportAgentAsync();
        var (_, adminClient) = await _factory.CreateAdminAsync();

        const string endpoint = "/api/v1/admin/support/tickets/";

        // Anonymous -> 401
        (await anonymousClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Player -> 401
        (await playerClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // VenueOwner -> 401
        (await ownerClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // SupportAgent -> 200 OK
        (await supportClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Admin -> 200 OK
        (await adminClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FinanceEndpoints_AllowFinanceAndAdmin_RejectPlayerAndVenueOwnerAsync()
    {
        // Arrange
        var anonymousClient = _factory.CreateClient();
        var (_, playerClient) = await _factory.CreatePlayerAsync();
        var (_, _, _, ownerClient) = await _factory.CreateVenueOwnerAsync();
        var (_, financeClient) = await _factory.CreateFinanceAsync();
        var (_, adminClient) = await _factory.CreateAdminAsync();

        const string endpoint = "/api/v1/admin/invoices/";

        // Anonymous -> 401
        (await anonymousClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Player -> 401
        (await playerClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // VenueOwner -> 401
        (await ownerClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Finance -> 200 OK
        (await financeClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Admin -> 200 OK
        (await adminClient.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
