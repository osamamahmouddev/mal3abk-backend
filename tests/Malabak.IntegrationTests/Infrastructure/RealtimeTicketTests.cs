using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// The WebSocket ticket flow of the booking hub (TASK-033), end to end on the test server with a real Redis: a short-lived
/// ticket is issued to an authenticated user, SignalR's negotiate step accepts it without using it up, and the WebSocket
/// connection that follows consumes it, so a captured ticket cannot open a second connection.
/// </summary>
public sealed class RealtimeTicketTests : IClassFixture<BookingApiFactory>
{
    private readonly BookingApiFactory _factory;

    public RealtimeTicketTests(BookingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ATicket_IsOnlyIssuedToAnAuthenticatedUserAsync()
    {
        var response = await _factory.CreateClient().PostAsync("/api/v1/realtime/ticket", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NegotiateAcceptsAValidTicketMoreThanOnce_AndOffersWebSocketsOnlyAsync()
    {
        var ticket = await IssueTicketAsync();
        var http = _factory.CreateClient();

        var first = await http.PostAsync(NegotiateUrl(ticket), content: null);
        var again = await http.PostAsync(NegotiateUrl(ticket), content: null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        again.StatusCode.Should().Be(HttpStatusCode.OK, "negotiate must not use the ticket up, or the connection that follows could never open");
        using var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("availableTransports").EnumerateArray()
            .Select(t => t.GetProperty("transport").GetString()).Should().Equal("WebSockets");
    }

    [Fact]
    public async Task NegotiateRefusesAMissingOrForgedTicketAsync()
    {
        var http = _factory.CreateClient();

        (await http.PostAsync("/hubs/booking/negotiate?negotiateVersion=1", content: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await http.PostAsync(NegotiateUrl("not.a.ticket"), content: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheWebSocketConnection_ConsumesTheTicket_SoItCannotOpenASecondOneAsync()
    {
        var ticket = await IssueTicketAsync();
        var http = _factory.CreateClient();
        using var negotiated = JsonDocument.Parse(await (await http.PostAsync(NegotiateUrl(ticket), content: null)).Content.ReadAsStringAsync());
        var connectionToken = negotiated.RootElement.GetProperty("connectionToken").GetString();
        var socketUri = new Uri($"ws://localhost/hubs/booking?id={connectionToken}&access_token={ticket}");

        using var socket = await _factory.Server.CreateWebSocketClient().ConnectAsync(socketUri, CancellationToken.None);
        socket.State.Should().Be(WebSocketState.Open);

        var replay = async () => await _factory.Server.CreateWebSocketClient().ConnectAsync(socketUri, CancellationToken.None);
        await replay.Should().ThrowAsync<Exception>("a ticket that already opened a connection must not open another");
    }

    [Fact]
    public async Task ASessionTokenIsNotATicket_AndATicketIsNotASessionTokenAsync()
    {
        var (_, player) = await _factory.CreatePlayerAsync();
        var sessionToken = player.DefaultRequestHeaders.Authorization!.Parameter!;

        var asTicket = await _factory.CreateClient().PostAsync(NegotiateUrl(sessionToken), content: null);
        asTicket.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the hub accepts tickets only, never ordinary access tokens");

        var ticket = await IssueTicketAsync();
        var asSession = _factory.CreateClient();
        asSession.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ticket);
        (await asSession.GetAsync("/api/v1/me/bookings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a ticket must not work as an API token");
    }

    private async Task<string> IssueTicketAsync()
    {
        var (_, player) = await _factory.CreatePlayerAsync();
        var response = await player.PostAsync("/api/v1/realtime/ticket", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("ticket").GetString()!;
    }

    private static string NegotiateUrl(string ticket) => $"/hubs/booking/negotiate?negotiateVersion=1&access_token={ticket}";
}
