using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Realtime;
using Malabak.Api.Infrastructure.Security;

namespace Malabak.Api.Endpoints;

public static class RealtimeEndpoints
{
    public static void MapRealtimeEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/realtime")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization()
            .WithTags("Realtime");

        group.MapPost("/ticket", async (ClaimsPrincipal user, ITicketService ticketService, CancellationToken ct) =>
        {
            var result = await ticketService.CreateTicketAsync(user, ct);
            return Results.Ok(result);
        })
        .RequireDistributedRateLimiting("realtime-ticket", permitLimit: 30, windowSeconds: 60)
        .WithName("CreateRealtimeTicket")
        .WithSummary("Generate a 60-second, single-use WebSocket connection ticket");
    }
}
