using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Booking.Application.Admin;
using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>
/// Settlement with venues (commission model): the platform team sees what it owes each venue and records the transfers it makes;
/// a venue sees its own balance and payouts.
/// </summary>
public static class VenuePayoutEndpoints
{
    public static void MapVenuePayoutEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var admin = app.MapGroup("/api/v{version:apiVersion}/admin/payouts")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .AddEndpointFilter<AdminIpAllowlistFilter>()
            .RequireAuthorization(Permissions.AdminPaymentsManage)
            .WithTags("Admin Payouts");

        admin.MapGet("", async (HttpContext http, VenuePayoutService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return Results.Ok(await service.GetBalancesAsync(ct));
            })
            .Produces<IReadOnlyList<VenuePayoutBalance>>()
            .WithName("AdminGetVenuePayoutBalances")
            .WithSummary("What the platform owes each venue from the deposits it received, less what it already sent");

        admin.MapGet("/venues/{venueId:guid}", async (Guid venueId, HttpContext http, VenuePayoutService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.GetStatementAsync(venueId, ct));
            })
            .Produces<VenuePayoutStatement>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminGetVenuePayoutStatement")
            .WithSummary("A venue's balance, its payouts and the bookings behind them");

        admin.MapPost("/venues/{venueId:guid}", async (Guid venueId, [FromBody] RecordVenuePayoutApiRequest request, ClaimsPrincipal user,
                VenuePayoutService service, CancellationToken ct) =>
                AdminResults.From(await service.RecordAsync(venueId,
                    new RecordVenuePayoutInput(request.Amount, request.Method, request.Reference, request.Notes, request.PaidAtUtc),
                    user.GetUserId()!.Value, ct)))
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces<VenuePayoutItem>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminRecordVenuePayout")
            .WithSummary("Record a transfer made to the venue; it cannot exceed what the platform owes it");

        app.MapGet("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}/payouts", async (Guid venueId, HttpContext http,
                VenuePayoutService service, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "private, no-store";
                return AdminResults.From(await service.GetStatementAsync(venueId, ct));
            })
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .AddEndpointFilter(new VenueAccessEndpointFilter(Permissions.ReportsView))
            .WithTags("Venue Reports")
            .Produces<VenuePayoutStatement>()
            .WithName("GetVenuePayoutStatement")
            .WithSummary("What the platform owes this venue from app booking deposits, and the transfers it made");
    }
}

public sealed record RecordVenuePayoutApiRequest(decimal Amount, string? Method, string? Reference, string? Notes, DateTime? PaidAtUtc);
