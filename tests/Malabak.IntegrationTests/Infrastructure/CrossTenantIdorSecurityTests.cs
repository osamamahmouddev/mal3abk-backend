using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.SharedKernel;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class CrossTenantIdorSecurityTests : IClassFixture<BookingApiFactory>
{
    private readonly BookingApiFactory _factory;

    public CrossTenantIdorSecurityTests(BookingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task VenueManagement_CrossTenantAccess_ReturnsForbiddenOrNotFound_WithNoDataLeakAsync()
    {
        // Arrange: Create two separate venue owners (Tenant A and Tenant B)
        var (_, venueIdA, pitchIdA, _) = await _factory.CreateVenueOwnerAsync();
        var (_, _, _, clientB) = await _factory.CreateVenueOwnerAsync();

        // Endpoints belonging to Venue A accessed by Owner B
        var endpointsToTest = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, $"/api/v1/venue-manager/venues/{venueIdA}/pitches"),
            (HttpMethod.Get, $"/api/v1/venue-manager/venues/{venueIdA}/schedules"),
            (HttpMethod.Get, $"/api/v1/venue-manager/venues/{venueIdA}/price-rules/"),
            (HttpMethod.Get, $"/api/v1/venue-manager/venues/{venueIdA}/reports/?from=2026-11-01&to=2026-11-02"),
            (HttpMethod.Get, $"/api/v1/venue-manager/venues/{venueIdA}/staff/"),
            (HttpMethod.Get, $"/api/v1/venue-manager/venues/{venueIdA}/payment-methods/"),
            (HttpMethod.Get, $"/api/v1/venue-manager/venues/{venueIdA}/slots?date=2026-11-20"),
            (HttpMethod.Get, $"/api/v1/venue-manager/venues/{venueIdA}/pitches/{pitchIdA}/availability?date=2026-11-20")
        };

        foreach (var (method, path) in endpointsToTest)
        {
            using var request = new HttpRequestMessage(method, path);
            var response = await clientB.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            // Assert: Must be Forbidden (403) or Not Found (404), NEVER 200 OK.
            // Reason: IVenueAccessService denies access when user does not have permission on venueIdA, returning 403.
            response.StatusCode.Should().BeOneOf(
                [HttpStatusCode.Forbidden, HttpStatusCode.NotFound],
                $"cross-tenant access to '{path}' by an unauthorized venue owner must be rejected (SECURITY.md §2.3). Actual body: {body}");

            // Assert: Must NOT disclose resource details on IDOR attempts
            body.Should().NotContain(pitchIdA.ToString("D"), "response body must not leak tenant resource data on IDOR attempts");
        }
    }

    [Fact]
    public async Task Booking_CrossTenantAccess_ReturnsForbiddenOrNotFound_WithNoDataLeakAsync()
    {
        // Arrange: Create two players
        var (playerAId, _) = await _factory.CreatePlayerAsync();
        var (_, clientB) = await _factory.CreatePlayerAsync();
        var (_, venueId, pitchId, _) = await _factory.CreateVenueOwnerAsync();

        // Seed a booking owned by Player A
        var bookingIdA = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var start = now.Date.AddDays(2).AddHours(18);
        var bookingA = new BookingBuilder()
            .WithId(bookingIdA)
            .ForPlayer(playerAId)
            .ForVenue(venueId)
            .ForPitch(pitchId)
            .WithTimeSlot(start, start.AddHours(1))
            .Build();

        await _factory.SaveBookingAsync(bookingA);

        // Player B attempts to access or manipulate Player A's booking
        var endpointsToTest = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, $"/api/v1/bookings/{bookingIdA}/payment-methods"),
            (HttpMethod.Get, $"/api/v1/bookings/{bookingIdA}/cancel/preview"),
            (HttpMethod.Get, $"/api/v1/bookings/{bookingIdA}/proofs"),
            (HttpMethod.Post, $"/api/v1/bookings/{bookingIdA}/cancel")
        };

        foreach (var (method, path) in endpointsToTest)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post)
            {
                request.Content = new StringContent("{\"reason\":\"malicious cancel\"}", System.Text.Encoding.UTF8, "application/json");
            }

            var response = await clientB.SendAsync(request);

            // Assert: Must be Forbidden (403) or Not Found (404)
            // Reason: Booking domain handlers verify caller identity against booking.PlayerId, returning 403/404 if mismatched.
            response.StatusCode.Should().BeOneOf(
                [HttpStatusCode.Forbidden, HttpStatusCode.NotFound],
                $"cross-tenant access to booking '{path}' by another player must be rejected");

            // Assert: Must NOT return player A booking details or customer PII
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(bookingA.BookingReference, "cross-tenant booking access must not leak business booking reference");
            body.Should().NotContain(playerAId.ToString("D"), "cross-tenant booking access must not leak player ID");
        }
    }

    [Fact]
    public async Task CustomerDetails_CrossTenantAccess_ReturnsForbiddenOrNotFoundAsync()
    {
        // Arrange: Tenant A creates a venue and seeds a booking with a customer
        var (playerAId, _) = await _factory.CreatePlayerAsync();
        var (_, venueIdA, pitchIdA, _) = await _factory.CreateVenueOwnerAsync();
        var (_, _, _, clientB) = await _factory.CreateVenueOwnerAsync();

        var bookingId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var start = now.Date.AddDays(3).AddHours(19);
        var bookingA = new BookingBuilder()
            .WithId(bookingId)
            .ForPlayer(playerAId)
            .ForVenue(venueIdA)
            .ForPitch(pitchIdA)
            .WithTimeSlot(start, start.AddHours(1))
            .Build();

        await _factory.SaveBookingAsync(bookingA);

        // Path to query customers of Venue A using playerAId as customerKey
        var path = $"/api/v1/venue-manager/venues/{venueIdA}/customers/{playerAId}";

        // Act: Owner B queries Customer of Venue A
        var response = await clientB.GetAsync(path);

        // Assert: Must return 403 Forbidden (Owner B does not have permissions on Venue A)
        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Forbidden, HttpStatusCode.NotFound],
            "cross-tenant customer access must return 403 or 404");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(playerAId.ToString("D"), "cross-tenant customer query must not leak customer ID");
    }

    [Fact]
    public async Task Payouts_CrossTenantAccess_ReturnsForbiddenOrNotFoundAsync()
    {
        // Arrange: Two venue owners
        var (_, venueIdA, _, clientA) = await _factory.CreateVenueOwnerAsync();
        var (_, _, _, clientB) = await _factory.CreateVenueOwnerAsync();
        var (_, playerClient) = await _factory.CreatePlayerAsync();
        var venuePayoutPath = $"/api/v1/venue-manager/venues/{venueIdA}/payouts";

        // 1. Each venue sees its own payouts; Owner B asking for Venue A's gets nothing
        (await clientA.GetAsync(venuePayoutPath)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientB.GetAsync(venuePayoutPath)).StatusCode.Should().BeOneOf(
            [HttpStatusCode.Forbidden, HttpStatusCode.NotFound],
            "Owner B must not see what the platform owes Venue A");

        // 2. Privilege boundary: a player or a venue owner cannot reach the admin payouts
        (await playerClient.GetAsync("/api/v1/admin/payouts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await clientA.PostAsJsonAsync($"/api/v1/admin/payouts/venues/{venueIdA}", new { amount = 10, method = "InstaPay", reference = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a venue cannot record a payout to itself");
    }

    [Fact]
    public async Task GetVenueReports_MissingRequiredParameters_ShouldReturn400BadRequestAsync()
    {
        var (_, venueIdA, _, clientA) = await _factory.CreateVenueOwnerAsync();

        // Calling /reports/ without required 'from' and 'to' query parameters
        var response = await clientA.GetAsync($"/api/v1/venue-manager/venues/{venueIdA}/reports/");

        // Expected: 400 Bad Request with ProblemDetails
        // Actual (BUG-001): 500 Internal Server Error with BadHttpRequestException
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
