using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Venues.Application.Policies;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class BookingPolicyEndpoints
{
    public static void MapBookingPolicyEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var manager = app.MapGroup("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}/booking-policy")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .WithTags("Venue Booking Policy");

        manager.MapGet("/", async (Guid venueId, ClaimsPrincipal user,
            IQueryHandler<GetBookingPoliciesQuery, Result<IReadOnlyList<BookingPolicyDto>>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new GetBookingPoliciesQuery(venueId, user.GetUserId()!.Value), ct)))
        .WithName("GetBookingPolicies")
        .WithSummary("Current booking policy and its version history");

        manager.MapPut("/", async (Guid venueId, ClaimsPrincipal user, [FromBody] BookingPolicyApiRequest request,
            ICommandHandler<SetBookingPolicyCommand, Result<BookingPolicyDto>> handler, CancellationToken ct) =>
        {
            // The deposit is set by the platform team (commission model); the handler keeps the venue's current one.
            var terms = new BookingPolicyTerms(BookingPolicyTerms.DefaultDeposit, request.ProofSubmissionWindowMinutes, request.ManualReviewWindowMinutes,
                request.CancellationDeadlineHours, request.RefundEligibility, request.RefundPercentage, request.NoShowRule,
                request.LateArrivalGraceMinutes, request.AllowReschedule, request.RescheduleDeadlineHours,
                request.PendingBookingsBlockSlots, request.Terms);

            return ToResult(await handler.HandleAsync(new SetBookingPolicyCommand(venueId, user.GetUserId()!.Value, terms), ct));
        })
        .WithName("SetBookingPolicy")
        .WithSummary("Publish a new policy version (applies to new bookings only). The deposit is set by the platform and kept as is");

        // Customer-facing preview for any signed-in user, limited to approved + published venues.
        app.MapGet("/api/v{version:apiVersion}/venues/{venueId:guid}/booking-policy", async (Guid venueId,
            IQueryHandler<GetBookingPolicyPreviewQuery, Result<BookingPolicyPreviewDto>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new GetBookingPolicyPreviewQuery(venueId), ct)))
        .WithApiVersionSet(apiVersionSet)
        .MapToApiVersion(1, 0)
        .RequireAuthorization()
        .WithTags("Venues")
        .WithName("GetBookingPolicyPreview")
        .WithSummary("What the player agrees to when booking this venue");
    }

    private static IResult ToResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var status = result.Error.Code.EndsWith(".NotFound", StringComparison.Ordinal)
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }

    /// <param name="DepositType">Ignored: the platform team sets the deposit. Kept so existing clients still validate.</param>
    /// <param name="DepositValue">Ignored, as <paramref name="DepositType"/>.</param>
    public sealed record BookingPolicyApiRequest(
        DepositType? DepositType,
        decimal? DepositValue,
        int ProofSubmissionWindowMinutes,
        int ManualReviewWindowMinutes,
        int CancellationDeadlineHours,
        RefundEligibility RefundEligibility,
        decimal RefundPercentage,
        NoShowRule NoShowRule,
        int LateArrivalGraceMinutes,
        bool AllowReschedule,
        int RescheduleDeadlineHours,
        bool PendingBookingsBlockSlots,
        string? Terms);
}
