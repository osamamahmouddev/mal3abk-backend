using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Errors;
using Malabak.Api.Infrastructure.Idempotency;
using Malabak.Api.Infrastructure.Media;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Application.Bookings.CreateBooking;
using Malabak.Modules.Booking.Application.Bookings.ManualBooking;
using Malabak.Modules.Booking.Application.Bookings.NoShow;
using Malabak.Modules.Booking.Application.Bookings.PlayerQueries;
using Malabak.Modules.Booking.Application.Bookings.Proofs;
using Malabak.Modules.Booking.Application.Bookings.Refunds;
using Malabak.Modules.Booking.Application.Bookings.ReviewProof;
using Malabak.Modules.Booking.Application.Bookings.SubmitProof;
using Malabak.Modules.Booking.Application.Bookings.VenueQueries;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Venues.Application.Common;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        // -------------------------------------------------------------
        // 1. Player Booking Endpoints (/api/v1/bookings & /api/v1/me/bookings)
        // -------------------------------------------------------------
        var bookings = app.MapGroup("/api/v{version:apiVersion}/bookings")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .WithTags("Bookings");

        // Create Booking Hold (TASK-032)
        bookings.MapPost("/", async (
            ClaimsPrincipal user,
            [FromBody] CreateBookingApiRequest request,
            ICommandHandler<CreateBookingCommand, Result<CreateBookingResponse>> handler,
            CancellationToken ct) =>
        {
            var playerId = user.GetUserId()!.Value;
            var command = new CreateBookingCommand(
                request.PitchId,
                playerId,
                request.StartUtc,
                request.EndUtc,
                request.QuoteToken,
                request.ExpectedPrice,
                playerId.ToString("D"),
                request.CustomerName,
                request.CustomerPhone);

            var result = await handler.HandleAsync(command, ct);
            if (result.IsSuccess)
            {
                return Results.Created($"/api/v1/me/bookings/{result.Value.BookingId}", result.Value);
            }

            return result.Error.Code switch
            {
                "Booking.SlotOverlap" => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status409Conflict),
                "Booking.MaxPendingBookingsExceeded" => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status429TooManyRequests),
                "Booking.PriceMismatch" => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status400BadRequest)
            };
        })
        .RequireAuthorization(Permissions.BookingsCreate)
        .RequireIdempotency(required: true)
        .RequireDistributedRateLimiting("bookings-create", permitLimit: 15, windowSeconds: 60)
        .WithName("CreateBooking")
        .WithSummary("Hold a pitch slot for 15 minutes subject to GiST exclusion constraint and player quota");

        // Proof Upload URL (TASK-034)
        bookings.MapPost("/{id:guid}/proof/upload-url", async (
            Guid id,
            ClaimsPrincipal user,
            MediaService mediaService,
            IQueryHandler<GetPlayerBookingByIdQuery, Result<PlayerBookingDetailDto>> queryHandler,
            CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;
            var bookingResult = await queryHandler.HandleAsync(new GetPlayerBookingByIdQuery(id, userId), ct);
            if (bookingResult.IsFailure)
            {
                return Results.Problem(title: bookingResult.Error.Code, detail: bookingResult.Error.Description, statusCode: StatusCodes.Status404NotFound);
            }

            var booking = bookingResult.Value;
            if (!booking.AllowedActions.CanUploadProof)
            {
                return Results.Problem(title: "Booking.CannotUploadProof", detail: "Proof cannot be uploaded for this booking in its current state.", statusCode: StatusCodes.Status409Conflict);
            }

            var ticket = await mediaService.CreateUploadAsync(userId, MediaPurpose.PaymentProof, ct);
            return Results.Ok(ticket);
        })
        .RequireAuthorization()
        .WithName("GetProofUploadUrl")
        .WithSummary("Generate a 5-minute pre-signed upload URL for payment receipt");

        // Submit Proof (TASK-034, TASK-408)
        bookings.MapPost("/{id:guid}/proof/submit", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] SubmitProofApiRequest request,
            MediaService mediaService,
            ICommandHandler<SubmitProofCommand, Result> handler,
            CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;

            // Verify owned media asset is ready and of purpose PaymentProof
            var asset = await mediaService.FindOwnedReadyAsync(request.MediaId, userId, MediaPurpose.PaymentProof, ct);
            if (asset is null)
            {
                return Results.Problem(title: "Media.NotFound", detail: "Payment proof media was not found or has not finished processing.", statusCode: StatusCodes.Status404NotFound);
            }

            var fileKey = MediaService.StorageKey(asset, "large");

            // The receipt's content hash, recorded when the upload was processed. Without it duplicate detection cannot
            // work, so a proof is refused rather than accepted unchecked (assets from before hashing must be re-uploaded).
            var sha256 = asset.ContentSha256;
            if (string.IsNullOrWhiteSpace(sha256))
            {
                return Results.Problem(title: "Proof.HashUnavailable", detail: "This upload cannot be verified. Please upload the receipt again.", statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var command = new SubmitProofCommand(
                id,
                userId,
                request.MediaId,
                fileKey,
                $"proof_{asset.Id:N}.webp",
                "image/webp",
                asset.SizeBytes ?? 0,
                sha256,
                request.Notes);

            var result = await handler.HandleAsync(command, ct);
            if (result.IsSuccess)
            {
                return Results.Ok(new { message = "Proof submitted successfully", bookingId = id });
            }

            return result.Error.Code switch
            {
                "Proof.DuplicateReceipt" => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status409Conflict),
                "Booking.HoldExpired" => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status410Gone),
                _ => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status400BadRequest)
            };
        })
        .RequireAuthorization()
        .WithName("SubmitPaymentProof")
        .WithSummary("Submit processed payment proof receipt for a booking hold");

        // Cancel Booking (TASK-037)
        bookings.MapPost("/{id:guid}/cancel", async (
            Guid id,
            ClaimsPrincipal user,
            [FromBody] CancelBookingApiRequest request,
            ICommandHandler<CancelBookingCommand, Result> handler,
            CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;
            var command = new CancelBookingCommand(id, userId, request.Reason);
            var result = await handler.HandleAsync(command, ct);

            if (result.IsSuccess)
            {
                return Results.Ok(new { message = "Booking cancelled successfully", bookingId = id });
            }

            return result.Error.Code switch
            {
                "Booking.CancellationWindowClosed" => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status409Conflict),
                "Booking.NotFound" => Results.NotFound(),
                _ => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status400BadRequest)
            };
        })
        .RequireAuthorization()
        .WithName("CancelBooking")
        .WithSummary("Cancel booking if within cancellation policy deadline");

        // Cancel Preview (TASK-406)
        bookings.MapGet("/{id:guid}/cancel/preview", async (
            Guid id,
            ClaimsPrincipal user,
            IQueryHandler<GetCancelPreviewQuery, Result<CancelPreviewResponse>> handler,
            CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;
            var result = await handler.HandleAsync(new GetCancelPreviewQuery(id, userId), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
        })
        .RequireAuthorization()
        .WithName("GetCancelPreview")
        .WithSummary("Preview cancellation eligibility and estimated refund amount");

        // Proof History & Signed Downloads (TASK-403, TASK-408)
        bookings.MapGet("/{id:guid}/proofs", async (
            Guid id,
            ClaimsPrincipal user,
            MediaService mediaService,
            IQueryHandler<GetBookingProofsQuery, Result<IReadOnlyList<BookingProofDto>>> handler,
            CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;
            var result = await handler.HandleAsync(new GetBookingProofsQuery(id, userId), ct);
            if (result.IsFailure)
            {
                return Results.NotFound();
            }

            var proofsWithSignedUrls = result.Value.Select(p => new
            {
                p.Id,
                p.BookingId,
                p.OriginalFileName,
                p.ContentType,
                p.FileSizeBytes,
                p.IsScanClean,
                p.IsApproved,
                p.RejectionReason,
                p.UploadedAtUtc,
                DownloadUrl = p.MediaId is { } mediaId ? mediaService.SignedUrl(mediaId, "large") : null
            });

            return Results.Ok(proofsWithSignedUrls);
        })
        .RequireAuthorization()
        .WithName("GetBookingProofs")
        .WithSummary("Get payment proofs with 5-minute authorized signed download URLs");

        // -------------------------------------------------------------
        // 2. Player My-Bookings Endpoints (/api/v1/me/bookings) (TASK-401)
        // -------------------------------------------------------------
        var meBookings = app.MapGroup("/api/v{version:apiVersion}/me/bookings")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization()
            .WithTags("My Bookings");

        meBookings.MapGet("/", async (
            ClaimsPrincipal user,
            [FromQuery] string? tab,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            IQueryHandler<GetPlayerBookingsQuery, Result<PagedList<PlayerBookingSummaryDto>>> handler,
            CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;
            var result = await handler.HandleAsync(new GetPlayerBookingsQuery(userId, tab, page, pageSize), ct);
            return Results.Ok(result.Value);
        })
        .WithName("GetMyBookings")
        .WithSummary("List player bookings filtered by tab (upcoming, pending, completed, cancelled, all)");

        meBookings.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            IQueryHandler<GetPlayerBookingByIdQuery, Result<PlayerBookingDetailDto>> handler,
            CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;
            var result = await handler.HandleAsync(new GetPlayerBookingByIdQuery(id, userId), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.NotFound();
        })
        .WithName("GetMyBookingDetail")
        .WithSummary("Get booking detail drawer payload with server-computed allowed actions");

        // -------------------------------------------------------------
        // 3. Venue Manager Booking & Calendar Endpoints (/api/v1/venue-manager/venues/{venueId}) (TASK-404, TASK-035, TASK-405, TASK-406)
        // -------------------------------------------------------------
        var venueManager = app.MapGroup("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .WithTags("Venue Bookings & Calendar");

        // Reservations list with filters
        venueManager.MapGet("/bookings", async (
            Guid venueId,
            [FromQuery] BookingStatus? status,
            [FromQuery] DateTime? startDateUtc,
            [FromQuery] DateTime? endDateUtc,
            [FromQuery] Guid? pitchId,
            [FromQuery] string? search,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            IQueryHandler<GetVenueBookingsQuery, Result<PagedList<VenueBookingItemDto>>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetVenueBookingsQuery(venueId, status, startDateUtc, endDateUtc, pitchId, search, page, pageSize), ct);
            return Results.Ok(result.Value);
        })
        .WithName("GetVenueBookings")
        .WithSummary("List venue bookings with status, date, pitch and customer filters");

        // Pending bookings awaiting review
        venueManager.MapGet("/bookings/pending", async (
            Guid venueId,
            IQueryHandler<GetVenuePendingBookingsQuery, Result<IReadOnlyList<VenueBookingItemDto>>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetVenuePendingBookingsQuery(venueId), ct);
            return Results.Ok(result.Value);
        })
        .WithName("GetVenuePendingBookings")
        .WithSummary("List pending reservations awaiting review or deposit");

        // Venue booking detail
        venueManager.MapGet("/bookings/{id:guid}", async (
            Guid venueId,
            Guid id,
            MediaService mediaService,
            IQueryHandler<GetVenueBookingDetailQuery, Result<VenueBookingDetailDto>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetVenueBookingDetailQuery(venueId, id), ct);
            if (result.IsFailure)
            {
                return Results.NotFound();
            }

            // Staff review the receipt image, so each proof gets a short-lived signed link (5 minutes).
            var detail = result.Value;
            var proofs = detail.Proofs
                .Select(p => p with { DownloadUrl = p.MediaId is { } mediaId ? mediaService.SignedUrl(mediaId, "large") : null })
                .ToList();
            return Results.Ok(detail with { Proofs = proofs });
        })
        .WithName("GetVenueBookingDetail")
        .WithSummary("Get detailed reservation info including proof history and status audit");

        // Calendar Day/Week/Month view
        venueManager.MapGet("/calendar", async (
            Guid venueId,
            [FromQuery] DateTime rangeStartUtc,
            [FromQuery] DateTime rangeEndUtc,
            [FromQuery] Guid? pitchId,
            IQueryHandler<GetVenueCalendarQuery, Result<IReadOnlyList<CalendarSlotDto>>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetVenueCalendarQuery(venueId, rangeStartUtc, rangeEndUtc, pitchId), ct);
            return Results.Ok(result.Value);
        })
        .WithName("GetVenueCalendar")
        .WithSummary("Calendar view of all bookings, holds, and manual reservations for a date range");

        // Review Payment Proof (Approve / Reject) (TASK-035)
        venueManager.MapPost("/bookings/{id:guid}/proof/review", async (
            Guid venueId,
            Guid id,
            ClaimsPrincipal user,
            [FromBody] ReviewProofApiRequest request,
            HttpContext httpContext,
            ICommandHandler<ReviewProofCommand, Result> handler,
            CancellationToken ct) =>
        {
            var actorUserId = user.GetUserId()!.Value;
            var command = new ReviewProofCommand(id, venueId, actorUserId, request.Approved, request.RejectionReason);
            var result = await handler.HandleAsync(command, ct);

            var message = request.Approved ? "Booking confirmed" : "Proof rejected";
            return result.ToHttpResult(httpContext: httpContext, onSuccess: () => Results.Ok(new { message, bookingId = id }));
        })
        .RequireAuthorization(Permissions.BookingsApprove)
        .WithName("ReviewPaymentProof")
        .WithSummary("Approve or reject a submitted payment proof");

        // Create Manual Booking by Venue Staff (TASK-037)
        venueManager.MapPost("/bookings/manual", async (
            Guid venueId,
            ClaimsPrincipal user,
            [FromBody] CreateManualBookingApiRequest request,
            ICommandHandler<CreateManualBookingCommand, Result<ManualBookingResponse>> handler,
            CancellationToken ct) =>
        {
            var actorUserId = user.GetUserId()!.Value;
            var command = new CreateManualBookingCommand(
                venueId,
                request.PitchId,
                request.StartUtc,
                request.EndUtc,
                request.Price,
                request.Currency ?? "EGP",
                request.CustomerName,
                request.CustomerPhone,
                actorUserId.ToString("D"),
                request.Notes);

            var result = await handler.HandleAsync(command, ct);
            if (result.IsSuccess)
            {
                return Results.Created($"/api/v1/venue-manager/venues/{venueId}/bookings/{result.Value.BookingId}", result.Value);
            }

            return result.Error.Code switch
            {
                "Booking.SlotOverlap" => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status400BadRequest)
            };
        })
        .WithName("CreateManualBooking")
        .WithSummary("Create manual phone/walk-in reservation directly into Confirmed state under GiST protection");

        // Mark No-Show (TASK-405)
        venueManager.MapPost("/bookings/{id:guid}/no-show", async (
            Guid venueId,
            Guid id,
            ClaimsPrincipal user,
            [FromBody] NoShowApiRequest request,
            HttpContext httpContext,
            ICommandHandler<MarkNoShowCommand, Result> handler,
            CancellationToken ct) =>
        {
            var actorUserId = user.GetUserId()!.Value;
            var result = await handler.HandleAsync(new MarkNoShowCommand(venueId, id, request.Reason, actorUserId), ct);
            return result.ToHttpResult(httpContext: httpContext, onSuccess: () => Results.Ok(new { message = "Booking marked as NoShow", bookingId = id }));
        })
        .WithName("MarkBookingNoShow")
        .WithSummary("Mark confirmed booking as No-Show after start time");

        // Record Manual Refund (TASK-406)
        venueManager.MapPost("/bookings/{id:guid}/refund/record", async (
            Guid venueId,
            Guid id,
            ClaimsPrincipal user,
            [FromBody] RecordRefundApiRequest request,
            HttpContext httpContext,
            ICommandHandler<RecordManualRefundCommand, Result> handler,
            CancellationToken ct) =>
        {
            var actorUserId = user.GetUserId()!.Value;
            var result = await handler.HandleAsync(new RecordManualRefundCommand(venueId, id, request.Amount, request.Notes ?? string.Empty, actorUserId), ct);
            return result.ToHttpResult(httpContext: httpContext, onSuccess: () => Results.Ok(new { message = "Refund recorded successfully", bookingId = id }));
        })
        .WithName("RecordManualRefund")
        .WithSummary("Record manual refund processed by venue for cancelled reservation");
    }

    public sealed record CreateBookingApiRequest(
        Guid PitchId,
        DateTime StartUtc,
        DateTime EndUtc,
        string QuoteToken,
        decimal ExpectedPrice,
        string? CustomerName = null,
        string? CustomerPhone = null);

    public sealed record SubmitProofApiRequest(
        Guid MediaId,
        string? Notes = null);

    public sealed record CancelBookingApiRequest(
        string? Reason = null);

    public sealed record ReviewProofApiRequest(
        bool Approved,
        string? RejectionReason = null);

    public sealed record CreateManualBookingApiRequest(
        Guid PitchId,
        DateTime StartUtc,
        DateTime EndUtc,
        decimal Price,
        string? Currency,
        string CustomerName,
        string? CustomerPhone,
        string? Notes = null);

    public sealed record NoShowApiRequest(string? Reason = null);

    public sealed record RecordRefundApiRequest(decimal Amount, string? Notes = null);
}
