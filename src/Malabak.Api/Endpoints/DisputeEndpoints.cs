using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Booking.Application.Disputes;
using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Payment proof disputes (TASK-704): opened by players or venues on their own bookings, decided by platform staff.</summary>
public static class DisputeEndpoints
{
    public static void MapDisputeEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0);

        var player = v1.MapGroup("/bookings/{bookingId:guid}/dispute").RequireAuthorization(Permissions.BookingsCreate).WithTags("Booking Disputes");
        player.MapGet("", async (Guid bookingId, ClaimsPrincipal user, HttpContext http, PaymentDisputeService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.GetForPlayerAsync(bookingId, user.GetUserId()!.Value, ct));
            })
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("PlayerGetBookingDispute").WithSummary("The latest dispute on my booking");
        player.MapPost("", async (Guid bookingId, [FromBody] OpenDisputeApiRequest request, ClaimsPrincipal user, PaymentDisputeService service, CancellationToken ct) =>
                AdminResults.From(await service.OpenByPlayerAsync(bookingId, user.GetUserId()!.Value, request.Reason, ct)))
            .RequireDistributedRateLimiting("dispute-open-player", permitLimit: 5, windowSeconds: 3600)
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("PlayerOpenBookingDispute").WithSummary("Dispute the payment decision on my booking (one open dispute per booking)");
        player.MapPost("/evidence", async (Guid bookingId, [FromBody] DisputeEvidenceApiRequest request, ClaimsPrincipal user, PaymentDisputeService service, CancellationToken ct) =>
                AdminResults.From(await service.AddPlayerEvidenceAsync(bookingId, user.GetUserId()!.Value, request.Note, request.ProofId, ct)))
            .RequireDistributedRateLimiting("dispute-evidence-player", permitLimit: 20, windowSeconds: 3600)
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("PlayerAddDisputeEvidence").WithSummary("Add a note or point to one of the booking's payment proofs");

        var venue = v1.MapGroup("/venue-manager/venues/{venueId:guid}/bookings/{bookingId:guid}/dispute")
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .WithTags("Venue Booking Disputes");
        venue.MapGet("", async (Guid venueId, Guid bookingId, HttpContext http, PaymentDisputeService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.GetForVenueAsync(bookingId, venueId, ct));
            })
            .RequireVenuePermission(Permissions.PaymentProofsReview)
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("VenueGetBookingDispute").WithSummary("The latest dispute on a booking of this venue");
        venue.MapPost("", async (Guid venueId, Guid bookingId, [FromBody] OpenDisputeApiRequest request, ClaimsPrincipal user, PaymentDisputeService service, CancellationToken ct) =>
                AdminResults.From(await service.OpenByVenueAsync(bookingId, venueId, user.GetUserId()!.Value, request.Reason, ct)))
            .RequireVenuePermission(Permissions.PaymentProofsReview)
            .RequireDistributedRateLimiting("dispute-open-venue", permitLimit: 20, windowSeconds: 3600)
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("VenueOpenBookingDispute").WithSummary("Open a dispute about a booking's payment (e.g. a suspicious proof)");
        venue.MapPost("/evidence", async (Guid venueId, Guid bookingId, [FromBody] DisputeEvidenceApiRequest request, ClaimsPrincipal user, PaymentDisputeService service, CancellationToken ct) =>
                AdminResults.From(await service.AddVenueEvidenceAsync(bookingId, venueId, user.GetUserId()!.Value, request.Note, request.ProofId, ct)))
            .RequireVenuePermission(Permissions.PaymentProofsReview)
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("VenueAddDisputeEvidence").WithSummary("Add the venue's evidence to an open dispute");

        var admin = v1.MapGroup("/admin/disputes").RequireAuthorization(Permissions.AdminDisputesResolve)
            .AddEndpointFilter<AdminIpAllowlistFilter>().WithTags("Admin Disputes");
        admin.MapGet("", async ([FromQuery] string? status, [FromQuery] Guid? venueId, [FromQuery] int? page, [FromQuery] int? pageSize,
                HttpContext http, PaymentDisputeService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.QueueAsync(status, venueId, page ?? 1, pageSize ?? 25, ct));
            })
            .Produces<DisputePage>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminDisputeQueue").WithSummary("Unresolved disputes, oldest first (or filter by status)");
        admin.MapGet("/{disputeId:guid}", async (Guid disputeId, HttpContext http, PaymentDisputeService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.GetAsync(disputeId, ct));
            })
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminGetDispute").WithSummary("Dispute with both sides' evidence");
        admin.MapPost("/{disputeId:guid}/review", async (Guid disputeId, ClaimsPrincipal user, PaymentDisputeService service, CancellationToken ct) =>
                AdminResults.From(await service.StartReviewAsync(disputeId, user.GetUserId()!.Value, ct)))
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminStartDisputeReview").WithSummary("Take the dispute (marks it under review by me)");
        admin.MapPost("/{disputeId:guid}/notes", async (Guid disputeId, [FromBody] DisputeEvidenceApiRequest request, ClaimsPrincipal user, PaymentDisputeService service, CancellationToken ct) =>
                AdminResults.From(await service.AddAdminNoteAsync(disputeId, user.GetUserId()!.Value, request.Note, ct)))
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminAddDisputeNote").WithSummary("Add a reviewer note to the evidence");
        admin.MapPost("/{disputeId:guid}/resolve", async (Guid disputeId, [FromBody] ResolveDisputeApiRequest request, ClaimsPrincipal user, PaymentDisputeService service, CancellationToken ct) =>
                AdminResults.From(await service.ResolveAsync(disputeId, request.Outcome, request.Note, user.GetUserId()!.Value, ct)))
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces<DisputeDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminResolveDispute")
            .WithSummary("Decide: ConfirmBooking, RefundPlayer (cancel + full deposit owed), FavorVenue (reject a pending proof) or Dismiss; reason required");
    }
}

public sealed record OpenDisputeApiRequest(string? Reason);

public sealed record DisputeEvidenceApiRequest(string? Note, Guid? ProofId);

public sealed record ResolveDisputeApiRequest(string? Outcome, string? Note);
