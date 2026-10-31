using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Booking.Application.Customers;
using Malabak.Modules.Venues.Application.Common;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Venue customer directory and staff-only notes (TASK-503). Personal data: never cached.</summary>
public static class VenueCustomerEndpoints
{
    public static void MapVenueCustomerEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var customers = app.MapGroup("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}/customers")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .AddEndpointFilter(new VenueAccessEndpointFilter(Permissions.CustomersView))
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "private, no-store";
                return await next(context);
            })
            .WithTags("Venue Customers");

        customers.MapGet("", async (Guid venueId, [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize,
            VenueCustomerService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(venueId, search?.Length > 100 ? search[..100] : search, page ?? 1, pageSize ?? 20, ct)))
        .RequireDistributedRateLimiting("venue-customers", permitLimit: 60, windowSeconds: 60)
        .WithName("ListVenueCustomers")
        .WithSummary("Customers who booked at the venue, with booking counts, no-shows, cancellations and spend");

        customers.MapGet("/{customerKey}", async (Guid venueId, string customerKey, ClaimsPrincipal user, IVenueAccessService access,
            VenueCustomerService service, CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;
            var canDeleteAny = (await access.GetAccessAsync(userId, venueId, ct)).Can(Permissions.StaffManage);
            return ToResult(await service.GetAsync(venueId, customerKey, userId, canDeleteAny, ct));
        })
        .RequireDistributedRateLimiting("venue-customers", permitLimit: 60, windowSeconds: 60)
        .WithName("GetVenueCustomer")
        .WithSummary("Customer details: summary, last 50 bookings at this venue and staff notes");

        customers.MapPost("/{customerKey}/notes", async (Guid venueId, string customerKey, [FromBody] CustomerNoteApiRequest request,
            ClaimsPrincipal user, VenueCustomerService service, CancellationToken ct) =>
        {
            var result = await service.AddNoteAsync(venueId, customerKey, user.GetUserId()!.Value, request.Text, ct);
            return result.IsSuccess ? Results.Created($"/api/v1/venue-manager/venues/{venueId}/customers/{customerKey}", result.Value) : ToResult(result);
        })
        .RequireDistributedRateLimiting("venue-customer-notes", permitLimit: 30, windowSeconds: 60)
        .WithName("AddVenueCustomerNote")
        .WithSummary("Add a staff-only note about the customer (stored encrypted)");

        customers.MapDelete("/{customerKey}/notes/{noteId:guid}", async (Guid venueId, string customerKey, Guid noteId, ClaimsPrincipal user,
            IVenueAccessService access, VenueCustomerService service, CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;
            var canDeleteAny = (await access.GetAccessAsync(userId, venueId, ct)).Can(Permissions.StaffManage);
            var result = await service.DeleteNoteAsync(venueId, customerKey, noteId, userId, canDeleteAny, ct);
            return result.IsSuccess ? Results.NoContent() : ToResult(result);
        })
        .RequireDistributedRateLimiting("venue-customer-notes", permitLimit: 30, windowSeconds: 60)
        .WithName("DeleteVenueCustomerNote")
        .WithSummary("Delete a note (its author, or a manager who can manage staff)");
    }

    private static IResult ToResult(Result result)
    {
        var status = result.Error.Code switch
        {
            _ when result.Error == CustomerErrors.CannotDeleteNote => StatusCodes.Status403Forbidden,
            _ when result.Error.Code.EndsWith("NotFound", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }

    private static IResult ToResult<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : ToResult((Result)result);
}

public sealed record CustomerNoteApiRequest(string Text);
