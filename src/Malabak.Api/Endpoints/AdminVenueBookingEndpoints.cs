using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Media;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Booking.Application.Admin;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Venues.Application.Admin;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Admin venue management (TASK-702) and cross-venue booking monitoring (TASK-703).</summary>
public static class AdminVenueBookingEndpoints
{
    public static void MapAdminVenueBookingEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/admin").WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0)
            .AddEndpointFilter<AdminIpAllowlistFilter>();
        MapVenues(v1.MapGroup("/venues").RequireAuthorization(Permissions.AdminVenuesApprove).WithTags("Admin Venues"));
        MapBookings(v1.MapGroup("/bookings").RequireAuthorization(Permissions.AdminBookingsView).WithTags("Admin Bookings"));
    }

    private static void MapVenues(RouteGroupBuilder venues)
    {
        venues.MapGet("", async ([FromQuery] string? q, [FromQuery] string? approvalStatus, [FromQuery] Guid? ownerId, [FromQuery] int? page, [FromQuery] int? pageSize,
                HttpContext http, AdminVenueService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.SearchAsync(new AdminVenueSearch(q, approvalStatus, ownerId, page ?? 1, pageSize ?? 25), ct));
            })
            .Produces<AdminVenuePage>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminSearchVenues").WithSummary("Search venues by name, city or area, approval status and owner");

        venues.MapGet("/{venueId:guid}", async (Guid venueId, HttpContext http, AdminVenueService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.GetAsync(venueId, ct));
            })
            .Produces<AdminVenueDetail>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminGetVenue").WithSummary("Venue with owner, pitch inventory, staff count, last-30-day booking figures and status history");

        venues.MapPut("/{venueId:guid}/admin-note", async (Guid venueId, [FromBody] AdminNoteApiRequest request, ClaimsPrincipal user, AdminVenueService service, CancellationToken ct) =>
                AdminResults.From(await service.SetAdminNoteAsync(venueId, request.Note, user.GetUserId()!.Value, ct)))
            .Produces<AdminVenueDetail>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminSetVenueNote").WithSummary("Internal admin note (never shown to owners or players)");

        venues.MapPost("/{venueId:guid}/suspend", async (Guid venueId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminVenueService service, CancellationToken ct) =>
                AdminResults.From(await service.SuspendAsync(venueId, request.Reason, user.GetUserId()!.Value, ct)))
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces<AdminVenueDetail>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminSuspendVenue").WithSummary("Hide an approved venue and stop new bookings (reason + step-up required; existing bookings stay)");

        venues.MapPost("/{venueId:guid}/reactivate", async (Guid venueId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminVenueService service, CancellationToken ct) =>
                AdminResults.From(await service.ReactivateAsync(venueId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<AdminVenueDetail>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminReactivateVenue").WithSummary("Lift a suspension; the venue returns to its previous published state");

        venues.MapPost("/{venueId:guid}/ask-owner", async (Guid venueId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminVenueService service, CancellationToken ct) =>
                AdminResults.From(await service.RequestInfoAsync(venueId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<AdminVenueDetail>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminAskVenueOwner").WithSummary("Ask the owner of a live venue for more information (no status change)");

        venues.MapPost("/{venueId:guid}/ownership", async (Guid venueId, [FromBody] TransferOwnershipApiRequest request, ClaimsPrincipal user, AdminVenueService service, CancellationToken ct) =>
                AdminResults.From(await service.TransferOwnershipAsync(venueId, request.NewOwnerId, request.Reason, user.GetUserId()!.Value, ct)))
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces<AdminVenueDetail>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminTransferVenueOwnership").WithSummary("Move the venue to another existing account (reason + step-up required)");
    }

    private static void MapBookings(RouteGroupBuilder bookings)
    {
        bookings.MapGet("", async ([FromQuery] string? reference, [FromQuery] Guid? venueId, [FromQuery] Guid? playerId, [FromQuery] string? status,
                [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] int? page, [FromQuery] int? pageSize,
                HttpContext http, AdminBookingService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.SearchAsync(new AdminBookingSearch(reference, venueId, playerId, status, fromUtc, toUtc, page ?? 1, pageSize ?? 25), ct));
            })
            .Produces<AdminBookingPage>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminSearchBookings").WithSummary("Search bookings across venues by reference, venue, player, status and start time");

        bookings.MapGet("/{bookingId:guid}", async (Guid bookingId, HttpContext http, AdminBookingService service, MediaService mediaService, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                var result = await service.GetAsync(bookingId, ct);
                if (result.IsFailure)
                {
                    return AdminResults.From(result);
                }

                var detail = result.Value;
                var proofs = detail.Proofs
                    .Select(p => p with { DownloadUrl = p.MediaId is { } mediaId ? mediaService.SignedUrl(mediaId, "large") : null })
                    .ToList();
                return Results.Ok(detail with { Proofs = proofs });
            })
            .Produces<AdminBookingDetail>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminGetBooking").WithSummary("Booking with status history, payment proofs (with signed download URLs) and refunds");

        bookings.MapPost("/{bookingId:guid}/force-cancel", async (Guid bookingId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminBookingService service, CancellationToken ct) =>
                AdminResults.From(await service.ForceCancelAsync(bookingId, request.Reason, user.GetUserId()!.Value, ct)))
            .RequireAuthorization(Permissions.AdminDisputesResolve)
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces<AdminBookingDetail>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminForceCancelBooking").WithSummary("Cancel any active booking with a reason; the full deposit is owed back to the player");

        bookings.MapGet("/deposit-reviews", async ([FromQuery] int? page, [FromQuery] int? pageSize,
                HttpContext http, AdminBookingService service, MediaService mediaService, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                var result = await service.GetPendingDepositReviewsAsync(page ?? 1, pageSize ?? 25, ct);
                if (result.IsFailure)
                {
                    return AdminResults.From(result);
                }

                var p = result.Value;
                var items = p.Items
                    .Select(i => i with { DownloadUrl = i.MediaId is { } mId ? mediaService.SignedUrl(mId, "large") : null })
                    .ToList();
                return Results.Ok(p with { Items = items });
            })
            .Produces<AdminDepositReviewPage>()
            .WithName("AdminGetPendingDepositReviews")
            .WithSummary("List bookings awaiting platform deposit proof review (ProofSubmitted or UnderReview)");

        bookings.MapPost("/{bookingId:guid}/review-deposit", async (Guid bookingId, [FromBody] AdminReviewDepositApiRequest request,
                ClaimsPrincipal user, AdminBookingService service, CancellationToken ct) =>
            {
                var result = await service.ReviewDepositAsync(bookingId, request.Approved, request.RejectionReason, user.GetUserId()!.Value, ct);
                return AdminResults.From(result);
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminReviewDeposit")
            .WithSummary("Approve or reject a submitted booking deposit proof");

        bookings.MapPost("/{bookingId:guid}/mark-under-review", async (Guid bookingId,
                ClaimsPrincipal user, AdminBookingService service, CancellationToken ct) =>
            {
                var result = await service.MarkUnderReviewAsync(bookingId, user.GetUserId()!.Value, ct);
                return AdminResults.From(result);
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminMarkBookingUnderReview")
            .WithSummary("Mark a booking deposit proof as under review by admin staff");

        bookings.MapGet("/financial-summary", async (
                [FromQuery] DateTime? fromUtc,
                [FromQuery] DateTime? toUtc,
                HttpContext http,
                AdminBookingService service,
                CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                var result = await service.GetVenuesFinancialSummaryAsync(fromUtc, toUtc, ct);
                return AdminResults.From(result);
            })
            .Produces<IReadOnlyList<VenueFinancialSummaryItem>>()
            .WithName("AdminGetVenuesFinancialSummary")
            .WithSummary("Comprehensive financial and commission breakdown for all venues");

        bookings.MapPost("/refunds/{refundId:guid}/process", async (
                Guid refundId,
                [FromBody] AdminProcessRefundApiRequest? request,
                ClaimsPrincipal user,
                AdminBookingService service,
                CancellationToken ct) =>
            {
                var result = await service.ProcessRefundAsync(refundId, request?.Notes, user.GetUserId()!.Value, ct);
                return AdminResults.From(result);
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminProcessBookingRefund")
            .WithSummary("Record that platform administration processed and refunded deposit to the player");
    }
}

public sealed record AdminNoteApiRequest(string? Note);

public sealed record TransferOwnershipApiRequest(Guid NewOwnerId, string? Reason);

public sealed record AdminReviewDepositApiRequest(bool Approved, string? RejectionReason = null);

public sealed record AdminProcessRefundApiRequest(string? Notes = null);
