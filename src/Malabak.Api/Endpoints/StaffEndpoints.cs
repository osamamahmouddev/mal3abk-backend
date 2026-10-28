using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Venues.Application.Staff;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class StaffEndpoints
{
    public static void MapStaffEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var staff = app.MapGroup("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}/staff")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .WithTags("Venue Staff");

        staff.MapGet("/", async (Guid venueId, ClaimsPrincipal user, IQueryHandler<GetVenueStaffQuery, Result<VenueStaffDto>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new GetVenueStaffQuery(venueId, user.GetUserId()!.Value), ct)))
        .WithName("GetVenueStaff").WithSummary("Staff members and pending invitations");

        staff.MapPost("/invitations", async (Guid venueId, ClaimsPrincipal user, [FromBody] InviteStaffApiRequest request,
            ICommandHandler<InviteStaffCommand, Result<StaffInvitationDto>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new InviteStaffCommand(venueId, user.GetUserId()!.Value, request.Email, request.PhoneNumber, request.Role), ct),
                StatusCodes.Status201Created))
        .WithName("InviteStaff").WithSummary("Invite by email or phone; single-use link valid 48 hours");

        staff.MapPost("/invitations/{invitationId:guid}/resend", async (Guid venueId, Guid invitationId, ClaimsPrincipal user,
            ICommandHandler<ResendStaffInvitationCommand, Result> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new ResendStaffInvitationCommand(venueId, invitationId, user.GetUserId()!.Value), ct)))
        .WithName("ResendStaffInvitation").WithSummary("New link (the old one stops working) and a fresh 48 hours");

        staff.MapDelete("/invitations/{invitationId:guid}", async (Guid venueId, Guid invitationId, ClaimsPrincipal user,
            ICommandHandler<RevokeStaffInvitationCommand, Result> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new RevokeStaffInvitationCommand(venueId, invitationId, user.GetUserId()!.Value), ct)))
        .WithName("RevokeStaffInvitation");

        staff.MapPut("/members/{memberId:guid}", async (Guid venueId, Guid memberId, ClaimsPrincipal user, [FromBody] ChangeRoleApiRequest request,
            ICommandHandler<ChangeStaffRoleCommand, Result<StaffMemberDto>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new ChangeStaffRoleCommand(venueId, memberId, user.GetUserId()!.Value, request.Role), ct)))
        .WithName("ChangeStaffRole");

        staff.MapDelete("/members/{memberId:guid}", async (Guid venueId, Guid memberId, ClaimsPrincipal user,
            ICommandHandler<RemoveStaffMemberCommand, Result> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new RemoveStaffMemberCommand(venueId, memberId, user.GetUserId()!.Value), ct)))
        .WithName("RemoveStaffMember");

        // The invitee isn't staff yet, so acceptance only needs a signed-in user; the invitation must match their verified contact.
        app.MapPost("/api/v{version:apiVersion}/venue-invitations/accept", async (ClaimsPrincipal user, [FromBody] AcceptInvitationApiRequest request,
            ICommandHandler<AcceptStaffInvitationCommand, Result<StaffMemberDto>> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new AcceptStaffInvitationCommand(request.Token, user.GetUserId()!.Value), ct)))
        .WithApiVersionSet(apiVersionSet)
        .MapToApiVersion(1, 0)
        .RequireAuthorization()
        .RequireRateLimiting(PasswordAuthEndpoints.RateLimitPolicy)
        .WithTags("Venue Staff")
        .WithName("AcceptStaffInvitation");
    }

    private static IResult ToResult(Result result) => result.IsSuccess ? Results.NoContent() : ToProblem(result.Error);

    private static IResult ToResult<T>(Result<T> result, int successStatus = StatusCodes.Status200OK) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : ToProblem(result.Error);

    private static IResult ToProblem(Error error)
    {
        var status = error.Code switch
        {
            var c when c.EndsWith("NotFound", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
            var c when c.EndsWith(".Conflict", StringComparison.Ordinal) || c == "Staff.QuotaReached" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(title: error.Code, detail: error.Description, statusCode: status);
    }

    public sealed record InviteStaffApiRequest(string? Email, string? PhoneNumber, VenueStaffRole Role);

    public sealed record ChangeRoleApiRequest(VenueStaffRole Role);

    public sealed record AcceptInvitationApiRequest(string Token);
}
