using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Venues.Domain;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// The booking flows (TASK-410, TASK-032) through the real HTTP API with real sign-ins: a player and a venue owner, each with
/// their own token, against PostgreSQL and Redis. Who may do what is decided by the server, so the strangers in these
/// tests are real users too.
/// </summary>
public sealed class BookingHttpFlowTests : IClassFixture<BookingApiFactory>
{
    private static int _slot;

    private readonly BookingApiFactory _factory;

    public BookingHttpFlowTests(BookingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EveryBookingEndpoint_RefusesAnonymousCallersAsync()
    {
        var anonymous = _factory.CreateClient();
        var id = Guid.NewGuid();

        (await anonymous.GetAsync("/api/v1/me/bookings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/me/bookings/{id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/v1/bookings/{id}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/v1/bookings/{id}/proof/submit", new { mediaId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/venue-manager/venues/{id}/bookings/pending")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FlowA_ThePlatformApprovesTheDeposit_ThePlayerSeesItConfirmed_AndTheVenueIsOwedItsShareAsync()
    {
        var (_, venueId, pitchId, ownerClient) = await _factory.CreateVenueOwnerAsync();
        var (playerId, player) = await _factory.CreatePlayerAsync();
        var (_, admin) = await _factory.CreateAdminAsync();
        var bookingId = await SeedProofSubmittedAsync(playerId, venueId, pitchId);

        var queue = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/bookings/deposit-reviews?pageSize=100");
        queue.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("bookingId").GetGuid()).Should().Contain(bookingId);

        var review = await admin.PostAsJsonAsync($"/api/v1/admin/bookings/{bookingId}/review-deposit", new { approved = true });
        review.StatusCode.Should().Be(HttpStatusCode.NoContent, await review.Content.ReadAsStringAsync());

        var detail = await player.GetFromJsonAsync<JsonElement>($"/api/v1/me/bookings/{bookingId}");
        detail.GetProperty("status").GetInt32().Should().Be((int)BookingStatus.Confirmed);
        var statement = await ownerClient.GetFromJsonAsync<JsonElement>($"/api/v1/venue-manager/venues/{venueId}/payouts");
        statement.GetProperty("bookings").EnumerateArray().Select(b => b.GetProperty("bookingId").GetGuid()).Should().Contain(bookingId);
    }

    [Fact]
    public async Task FlowB_ARejectionNeedsAReason_AndLeavesThePlayerAbleToSendANewProofAsync()
    {
        var (_, venueId, pitchId, _) = await _factory.CreateVenueOwnerAsync();
        var (playerId, player) = await _factory.CreatePlayerAsync();
        var (_, admin) = await _factory.CreateAdminAsync();
        var bookingId = await SeedProofSubmittedAsync(playerId, venueId, pitchId);
        var url = $"/api/v1/admin/bookings/{bookingId}/review-deposit";

        var withoutReason = await admin.PostAsJsonAsync(url, new { approved = false });
        withoutReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var rejected = await admin.PostAsJsonAsync(url, new { approved = false, rejectionReason = "Receipt is blurry" });
        rejected.StatusCode.Should().Be(HttpStatusCode.NoContent, await rejected.Content.ReadAsStringAsync());

        var detail = await player.GetFromJsonAsync<JsonElement>($"/api/v1/me/bookings/{bookingId}");
        detail.GetProperty("allowedActions").GetProperty("canUploadProof").GetBoolean().Should().BeTrue("the hold is still running");
    }

    [Fact]
    public async Task FlowC_ThePlayerCancels_TheBookingMovesToTheCancelledTab_AndASecondCancelIsRefusedAsync()
    {
        var (_, venueId, pitchId, _) = await _factory.CreateVenueOwnerAsync();
        var (playerId, player) = await _factory.CreatePlayerAsync();
        var bookingId = await SeedPendingAsync(playerId, venueId, pitchId);

        var first = await player.PostAsJsonAsync($"/api/v1/bookings/{bookingId}/cancel", new { reason = "Plans changed" });
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        var cancelledResponse = await player.GetAsync("/api/v1/me/bookings?tab=cancelled");
        cancelledResponse.StatusCode.Should().Be(HttpStatusCode.OK, await cancelledResponse.Content.ReadAsStringAsync());
        var cancelled = await cancelledResponse.Content.ReadFromJsonAsync<JsonElement>();
        cancelled.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).Should().Contain(bookingId);

        var second = await player.PostAsJsonAsync($"/api/v1/bookings/{bookingId}/cancel", new { reason = "again" });
        second.IsSuccessStatusCode.Should().BeFalse();
        ((int)second.StatusCode).Should().BeLessThan(500);
    }

    [Fact]
    public async Task Idor_ANotherPlayerCannotReadCancelOrInspectSomeoneElsesBookingAsync()
    {
        var (_, venueId, pitchId, _) = await _factory.CreateVenueOwnerAsync();
        var (playerId, _) = await _factory.CreatePlayerAsync();
        var (_, stranger) = await _factory.CreatePlayerAsync();
        var bookingId = await SeedPendingAsync(playerId, venueId, pitchId);

        (await stranger.GetAsync($"/api/v1/me/bookings/{bookingId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/v1/bookings/{bookingId}/proofs")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/v1/bookings/{bookingId}/cancel/preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.PostAsJsonAsync($"/api/v1/bookings/{bookingId}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.PostAsJsonAsync($"/api/v1/bookings/{bookingId}/proof/submit", new { mediaId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Idor_AnotherVenuesOwnerAndAPlayer_CannotTouchThisVenuesBookingsAsync()
    {
        var (_, venueId, pitchId, _) = await _factory.CreateVenueOwnerAsync();
        var (_, otherVenueId, otherPitchId, otherOwner) = await _factory.CreateVenueOwnerAsync();
        var (playerId, player) = await _factory.CreatePlayerAsync();
        var bookingId = await SeedProofSubmittedAsync(playerId, venueId, pitchId);

        // Another venue's owner, using their own venue id: the booking is simply not theirs.
        (await otherOwner.GetAsync($"/api/v1/venue-manager/venues/{otherVenueId}/bookings/{bookingId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherOwner.PostAsJsonAsync($"/api/v1/venue-manager/venues/{otherVenueId}/bookings/{bookingId}/proof/review", new { approved = true }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Using someone else's venue id in the URL: refused before any handler runs.
        var foreignVenue = await otherOwner.GetAsync($"/api/v1/venue-manager/venues/{venueId}/bookings/pending");
        foreignVenue.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        // A player is not venue staff at all.
        (await player.GetAsync($"/api/v1/venue-manager/venues/{venueId}/bookings/pending")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A manual booking in one's own venue cannot name another venue's pitch.
        var manual = await otherOwner.PostAsJsonAsync($"/api/v1/venue-manager/venues/{otherVenueId}/bookings/manual", new
        {
            pitchId,
            startUtc = NextSlot(),
            endUtc = NextSlot().AddHours(1),
            price = 200,
            customerName = "Walk-in",
        });
        manual.StatusCode.Should().Be(HttpStatusCode.NotFound);
        otherPitchId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ManualBooking_IsCreatedOverHttp_ShowsOnTheCalendar_AndTheSameSlotConflictsAsync()
    {
        var (_, venueId, pitchId, owner) = await _factory.CreateVenueOwnerAsync();
        var start = NextSlot();
        var body = new { pitchId, startUtc = start, endUtc = start.AddHours(1), price = 250, customerName = "Walk-in", customerPhone = "01000000000" };
        var url = $"/api/v1/venue-manager/venues/{venueId}/bookings/manual";

        var created = await owner.PostAsJsonAsync(url, body);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var again = await owner.PostAsJsonAsync(url, body);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var calendar = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/venue-manager/venues/{venueId}/calendar?rangeStartUtc={Uri.EscapeDataString(start.AddHours(-1).ToString("o"))}&rangeEndUtc={Uri.EscapeDataString(start.AddHours(2).ToString("o"))}");
        calendar.EnumerateArray().Should().ContainSingle(s => s.GetProperty("isManual").GetBoolean());
    }

    [Fact]
    public async Task ACalendarRangeThatIsBackwards_IsABadRequest_NotAServerErrorAsync()
    {
        var (_, venueId, _, owner) = await _factory.CreateVenueOwnerAsync();
        var start = NextSlot();

        var response = await owner.GetAsync(
            $"/api/v1/venue-manager/venues/{venueId}/calendar?rangeStartUtc={Uri.EscapeDataString(start.ToString("o"))}&rangeEndUtc={Uri.EscapeDataString(start.AddDays(-1).ToString("o"))}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatingABooking_NeedsAnIdempotencyKey_AndABadQuoteIsRefusedNotCrashedAsync()
    {
        var (_, player) = await _factory.CreatePlayerAsync();
        var start = NextSlot();
        var body = new { pitchId = Guid.NewGuid(), startUtc = start, endUtc = start.AddHours(1), quoteToken = "forged", expectedPrice = 100 };

        var withoutKey = await player.PostAsJsonAsync("/api/v1/bookings", body);
        withoutKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var forged = await player.SendAsync(request);
        ((int)forged.StatusCode).Should().BeInRange(400, 499, "a forged quote token is a client error");
    }

    [Fact]
    public async Task OnlyThePlatformTeam_ReviewsDeposits_TheVenueAndItsStaffCannotAsync()
    {
        var (ownerId, venueId, pitchId, ownerClient) = await _factory.CreateVenueOwnerAsync();
        var (playerId, _) = await _factory.CreatePlayerAsync();
        var accountant = await _factory.CreateStaffAsync(venueId, ownerId, VenueStaffRole.Accountant);
        var (_, finance) = await _factory.CreateFinanceAsync();
        var bookingId = await SeedProofSubmittedAsync(playerId, venueId, pitchId);
        var url = $"/api/v1/admin/bookings/{bookingId}/review-deposit";

        // The deposit was paid to the platform, so the venue's own review route is gone and the admin one is not theirs.
        (await ownerClient.PostAsJsonAsync($"/api/v1/venue-manager/venues/{venueId}/bookings/{bookingId}/proof/review", new { approved = true }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ownerClient.PostAsJsonAsync(url, new { approved = true })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await accountant.PostAsJsonAsync(url, new { approved = true })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await accountant.GetAsync($"/api/v1/venue-manager/venues/{venueId}/bookings/pending")).StatusCode.Should().Be(HttpStatusCode.OK, "staff still follow the booking");

        (await finance.PostAsJsonAsync(url, new { approved = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task TheListsWork_WithoutAnyPagingParametersAsync()
    {
        var (_, venueId, pitchId, owner) = await _factory.CreateVenueOwnerAsync();
        var (playerId, player) = await _factory.CreatePlayerAsync();
        var bookingId = await SeedPendingAsync(playerId, venueId, pitchId);

        var mine = await player.GetAsync("/api/v1/me/bookings");
        var theirs = await owner.GetAsync($"/api/v1/venue-manager/venues/{venueId}/bookings");

        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        theirs.StatusCode.Should().Be(HttpStatusCode.OK);
        (await theirs.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(b => b.GetProperty("id").GetGuid()).Should().Contain(bookingId);
    }

    private static DateTime NextSlot()
    {
        var n = Interlocked.Increment(ref _slot);
        return new DateTime(DateTime.UtcNow.Year + 7, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(n * 2);
    }

    private async Task<Guid> SeedPendingAsync(Guid playerId, Guid venueId, Guid pitchId)
    {
        var start = NextSlot();
        var booking = new BookingBuilder().ForPlayer(playerId).ForVenue(venueId).ForPitch(pitchId).WithTimeSlot(start, start.AddHours(1)).Build();
        await _factory.SaveBookingAsync(booking);
        return booking.Id;
    }

    private async Task<Guid> SeedProofSubmittedAsync(Guid playerId, Guid venueId, Guid pitchId)
    {
        var start = NextSlot();
        var booking = new BookingBuilder().ForPlayer(playerId).ForVenue(venueId).ForPitch(pitchId).WithTimeSlot(start, start.AddHours(1)).Build();
        booking.SubmitProof(Guid.NewGuid(), playerId.ToString("D"), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await _factory.SaveBookingAsync(booking);
        return booking.Id;
    }
}
