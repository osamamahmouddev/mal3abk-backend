using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Venues.Application.Admin;
using Malabak.Modules.Venues.Application.Approvals;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class VenueApprovalEndpoints
{
    public static void MapVenueApprovalEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        // Owner side (tenant-scoped like the rest of the venue-manager API)
        var owner = app.MapGroup("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}/approval")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .WithTags("Venue Approval");

        owner.MapGet("/", async (Guid venueId, ClaimsPrincipal user,
            IQueryHandler<GetVenueApprovalQuery, Result<VenueApprovalDto>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new GetVenueApprovalQuery(venueId, user.GetUserId()!.Value), ct)))
        .WithName("GetVenueApproval")
        .WithSummary("Approval status, visibility and the review conversation for a venue");

        owner.MapPost("/submit", async (Guid venueId, ClaimsPrincipal user, [FromBody] ApprovalMessageApiRequest? request,
            ICommandHandler<SubmitVenueForApprovalCommand, Result<VenueApprovalDto>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new SubmitVenueForApprovalCommand(venueId, user.GetUserId()!.Value, request?.Message), ct)))
        .WithName("SubmitVenueForApproval")
        .WithSummary("Submit (or resubmit / answer an info request) for admin approval");

        // Admin side
        var admin = app.MapGroup("/api/v{version:apiVersion}/admin/venues")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.AdminVenuesApprove)
            .AddEndpointFilter<AdminIpAllowlistFilter>()
            .WithTags("Admin Venue Approval");

        admin.MapGet("/pending-approval", async ([FromQuery] int? page, [FromQuery] int? pageSize,
            IQueryHandler<GetPendingVenueApprovalsQuery, Result<IReadOnlyList<PendingVenueApprovalDto>>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new GetPendingVenueApprovalsQuery(page ?? 1, pageSize ?? 20), ct)))
        .Produces<IReadOnlyList<PendingVenueApprovalDto>>()
        .WithName("GetPendingVenueApprovals")
        .WithSummary("Venues waiting for an approval decision, oldest first");

        // TASK-061 contract path; same queue as /pending-approval (kept for existing clients).
        admin.MapGet("/approval-queue", async ([FromQuery] int? page, [FromQuery] int? pageSize,
            IQueryHandler<GetPendingVenueApprovalsQuery, Result<IReadOnlyList<PendingVenueApprovalDto>>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new GetPendingVenueApprovalsQuery(page ?? 1, pageSize ?? 20), ct)))
        .Produces<IReadOnlyList<PendingVenueApprovalDto>>()
        .WithName("GetVenueApprovalQueue")
        .WithSummary("Venue approval queue, oldest submission first (approval status is separate from the owner's published status)");

        admin.MapPost("/{venueId:guid}/approve", (Guid venueId, ClaimsPrincipal user, [FromBody] ApprovalMessageApiRequest? request,
            ICommandHandler<DecideVenueApprovalCommand, Result<VenueApprovalDto>> handler, CancellationToken ct) =>
            DecideAsync(handler, venueId, user, VenueApprovalDecision.Approve, request?.Message, ct))
        .Produces<VenueApprovalDto>()
        .WithName("ApproveVenue");

        admin.MapPost("/{venueId:guid}/reject", (Guid venueId, ClaimsPrincipal user, [FromBody] ApprovalMessageApiRequest request,
            ICommandHandler<DecideVenueApprovalCommand, Result<VenueApprovalDto>> handler, CancellationToken ct) =>
            DecideAsync(handler, venueId, user, VenueApprovalDecision.Reject, request.Message, ct))
        .Produces<VenueApprovalDto>()
        .WithName("RejectVenue")
        .WithSummary("Reject a submitted venue (reason required)");

        admin.MapPost("/{venueId:guid}/request-info", (Guid venueId, ClaimsPrincipal user, [FromBody] ApprovalMessageApiRequest request,
            ICommandHandler<DecideVenueApprovalCommand, Result<VenueApprovalDto>> handler, CancellationToken ct) =>
            DecideAsync(handler, venueId, user, VenueApprovalDecision.RequestInfo, request.Message, ct))
        .Produces<VenueApprovalDto>()
        .WithName("RequestVenueInfo")
        .WithSummary("Ask the owner for more information (message required)");

        admin.MapPatch("/{venueId:guid}/commission", async (
            Guid venueId,
            ClaimsPrincipal user,
            [FromBody] UpdateVenueCommissionRequest request,
            ICommandHandler<UpdateVenueCommissionCommand, Result<VenueCommissionDto>> handler,
            CancellationToken ct) =>
        {
            var adminId = user.GetUserId() ?? Guid.Empty;
            var command = new UpdateVenueCommissionCommand(
                venueId,
                request.PricingModel,
                request.CustomCommissionEnabled,
                request.CommissionType,
                request.CommissionRate,
                adminId);

            return ToResult(await handler.HandleAsync(command, ct));
        })
        .Produces<VenueCommissionDto>()
        .WithName("UpdateVenueCommission")
        .WithSummary("Configure pricing model and platform commission for a specific venue");

        admin.MapGet("/{venueId:guid}/commission", async (
            Guid venueId,
            IQueryHandler<GetVenueCommissionQuery, Result<VenueCommissionDto>> handler,
            CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new GetVenueCommissionQuery(venueId), ct)))
        .Produces<VenueCommissionDto>()
        .WithName("GetVenueCommission")
        .WithSummary("Get the pricing model and platform commission configuration for a venue");
    }

    public sealed record UpdateVenueCommissionRequest(
        PricingModel PricingModel,
        bool CustomCommissionEnabled,
        CommissionType CommissionType,
        decimal? CommissionRate);

    private static async Task<IResult> DecideAsync(
        ICommandHandler<DecideVenueApprovalCommand, Result<VenueApprovalDto>> handler,
        Guid venueId, ClaimsPrincipal user, VenueApprovalDecision decision, string? message, CancellationToken ct)
    {
        var adminId = user.GetUserId();
        return adminId is null
            ? Results.Unauthorized()
            : ToResult(await handler.HandleAsync(new DecideVenueApprovalCommand(venueId, adminId.Value, decision, message), ct));
    }

    private static IResult ToResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var status = result.Error.Code switch
        {
            var c when c.EndsWith(".NotFound", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
            var c when c.EndsWith(".Conflict", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }

    public sealed record ApprovalMessageApiRequest(string? Message);
}
