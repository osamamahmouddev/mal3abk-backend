using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Venues.Application.Quotes;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Booking quote (TASK-305): price, deposit, policy summary and a quote token for the create-booking call.</summary>
public static class BookingQuoteEndpoints
{
    public static void MapBookingQuoteEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        app.MapGroup("/api/v{version:apiVersion}/bookings")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.BookingsCreate)
            .WithTags("Bookings")
            .MapPost("/quote", async (ClaimsPrincipal user, [FromBody] QuoteApiRequest request, BookingQuoteService quotes, CancellationToken ct) =>
            {
                var result = await quotes.QuoteAsync(user.GetUserId()!.Value, request.PitchId, request.StartUtc, request.EndUtc, ct);
                if (result.IsSuccess)
                {
                    return Results.Ok(result.Value);
                }

                var status = result.Error.Code switch
                {
                    var c when c.EndsWith("NotFound", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
                    "Quote.SlotUnavailable" => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest
                };
                return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
            })
            .RequireDistributedRateLimiting("booking-quote", permitLimit: 60, windowSeconds: 60)
            .WithName("QuoteBooking")
            .WithSummary("Server-side total, deposit, remaining balance, policy summary and a 15-minute quote token");
    }

    public sealed record QuoteApiRequest(Guid PitchId, DateTime StartUtc, DateTime EndUtc);
}
