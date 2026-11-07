using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Support.Domain;
using Malabak.Modules.Support.Infrastructure;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>
/// Support tickets (TASK-706): players (<c>/support/tickets</c>) and venue users (<c>/venue-manager/support/tickets</c>) open and
/// follow their own tickets; support agents work the queue under <c>/admin/support/tickets</c>.
/// </summary>
public static class SupportEndpoints
{
    public const int ComplaintsShown = 50;

    public static void MapSupportEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0);

        MapRequester(v1.MapGroup("/support/tickets").RequireAuthorization(Permissions.ProfileManage).WithTags("Support"), TicketRequesterKind.Player, "Player");
        MapRequester(v1.MapGroup("/venue-manager/support/tickets").RequireAuthorization(Permissions.VenuesManage).WithTags("Venue Support"), TicketRequesterKind.Venue, "Venue");
        MapAgent(v1.MapGroup("/admin/support/tickets").RequireAuthorization(Permissions.SupportTicketsManage).AddEndpointFilter<AdminIpAllowlistFilter>().WithTags("Admin Support"));

        v1.MapGet("/admin/venues/{venueId:guid}/complaints", async (Guid venueId, HttpContext http, IVenueComplaintsReader complaints, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return Results.Ok(await complaints.GetComplaintsAsync(venueId, ComplaintsShown, ct));
            })
            .RequireAuthorization(Permissions.AdminVenuesApprove)
            .AddEndpointFilter<AdminIpAllowlistFilter>()
            .Produces<IReadOnlyList<VenueComplaintDto>>()
            .WithTags("Admin Venues").WithName("AdminGetVenueComplaints").WithSummary("Complaints about the venue (venue-complaint support tickets), newest first");
    }

    private static void MapRequester(RouteGroupBuilder tickets, TicketRequesterKind kind, string prefix)
    {
        tickets.MapPost("", async ([FromBody] OpenTicketApiRequest request, ClaimsPrincipal user, SupportTicketService service, CancellationToken ct) =>
            {
                var result = await service.OpenAsync(user.GetUserId()!.Value, kind,
                    new OpenTicketInput(request.Category, request.Subject, request.Message, request.RelatedBookingId, request.RelatedVenueId), ct);
                return result.IsSuccess ? Results.Created($"/api/v1/support/tickets/{result.Value.Ticket.Id}", result.Value) : AdminResults.Problem(result.Error);
            })
            .RequireDistributedRateLimiting($"support-open-{prefix}", permitLimit: 5, windowSeconds: 3600)
            .Produces<TicketDetailDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName($"{prefix}OpenSupportTicket").WithSummary("Open a support ticket (booking, deposit, refund, venue complaint, account, team/match dispute, subscription)");

        tickets.MapGet("", async ([FromQuery] int? page, [FromQuery] int? pageSize, ClaimsPrincipal user, HttpContext http, SupportTicketService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return Results.Ok(await service.ListMineAsync(user.GetUserId()!.Value, page ?? 1, pageSize ?? 20, ct));
            })
            .Produces<TicketPage>().WithName($"{prefix}ListMySupportTickets").WithSummary("My tickets, most recent activity first");

        tickets.MapGet("/{ticketId:guid}", async (Guid ticketId, ClaimsPrincipal user, HttpContext http, SupportTicketService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.GetMineAsync(ticketId, user.GetUserId()!.Value, ct));
            })
            .Produces<TicketDetailDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithName($"{prefix}GetMySupportTicket").WithSummary("One of my tickets with its conversation (internal notes are never included)");

        tickets.MapPost("/{ticketId:guid}/messages", async (Guid ticketId, [FromBody] TicketMessageApiRequest request, ClaimsPrincipal user, SupportTicketService service, CancellationToken ct) =>
                AdminResults.From(await service.ReplyAsRequesterAsync(ticketId, user.GetUserId()!.Value, request.Message, ct)))
            .RequireDistributedRateLimiting($"support-reply-{prefix}", permitLimit: 30, windowSeconds: 3600)
            .Produces<TicketDetailDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .WithName($"{prefix}ReplySupportTicket").WithSummary("Reply on my ticket (reopens a resolved ticket)");
    }

    private static void MapAgent(RouteGroupBuilder tickets)
    {
        tickets.MapGet("", async ([FromQuery] string? status, [FromQuery] string? category, [FromQuery] string? priority, [FromQuery] Guid? assigneeId,
                [FromQuery] bool? unassigned, [FromQuery] Guid? requesterId, [FromQuery] Guid? venueId, [FromQuery] Guid? bookingId, [FromQuery] string? q,
                [FromQuery] int? page, [FromQuery] int? pageSize, HttpContext http, SupportTicketService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.SearchAsync(
                    new TicketFilter(status, category, priority, assigneeId, unassigned ?? false, requesterId, venueId, bookingId, q, page ?? 1, pageSize ?? 25), ct));
            })
            .Produces<TicketPage>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminSearchSupportTickets").WithSummary("Ticket queue: most urgent first, then longest waiting");

        tickets.MapGet("/{ticketId:guid}", async (Guid ticketId, HttpContext http, SupportTicketService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.GetAsync(ticketId, ct));
            })
            .Produces<TicketDetailDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminGetSupportTicket").WithSummary("Ticket with the full conversation including internal notes");

        tickets.MapPost("/{ticketId:guid}/messages", async (Guid ticketId, [FromBody] AgentMessageApiRequest request, ClaimsPrincipal user, SupportTicketService service, CancellationToken ct) =>
                AdminResults.From(await service.ReplyAsAgentAsync(ticketId, user.GetUserId()!.Value, request.Message, request.Internal, ct)))
            .Produces<TicketDetailDto>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminReplySupportTicket").WithSummary("Reply to the requester, or add an internal note (Internal = true)");

        tickets.MapPut("/{ticketId:guid}/assignee", async (Guid ticketId, [FromBody] AssignTicketApiRequest request, ClaimsPrincipal user, SupportTicketService service, CancellationToken ct) =>
                AdminResults.From(await service.AssignAsync(ticketId, request.AssigneeId, user.GetUserId()!.Value, ct)))
            .Produces<TicketDetailDto>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminAssignSupportTicket").WithSummary("Assign the ticket to an agent (null to unassign)");

        tickets.MapPut("/{ticketId:guid}/priority", async (Guid ticketId, [FromBody] TicketPriorityApiRequest request, ClaimsPrincipal user, SupportTicketService service, CancellationToken ct) =>
                AdminResults.From(await service.SetPriorityAsync(ticketId, request.Priority, user.GetUserId()!.Value, ct)))
            .Produces<TicketDetailDto>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminSetSupportTicketPriority").WithSummary("Change priority (Low, Normal, High, Urgent)");

        tickets.MapPost("/{ticketId:guid}/resolve", async (Guid ticketId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, SupportTicketService service, CancellationToken ct) =>
                AdminResults.From(await service.ResolveAsync(ticketId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<TicketDetailDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminResolveSupportTicket").WithSummary("Resolve with a reason the requester sees");

        tickets.MapPost("/{ticketId:guid}/close", async (Guid ticketId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, SupportTicketService service, CancellationToken ct) =>
                AdminResults.From(await service.CloseAsync(ticketId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<TicketDetailDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminCloseSupportTicket").WithSummary("Close for good (a reason is required unless already resolved)");
    }
}

public sealed record OpenTicketApiRequest(string? Category, string? Subject, string? Message, Guid? RelatedBookingId, Guid? RelatedVenueId);

public sealed record TicketMessageApiRequest(string? Message);

public sealed record AgentMessageApiRequest(string? Message, bool Internal);

public sealed record AssignTicketApiRequest(Guid? AssigneeId);

public sealed record TicketPriorityApiRequest(string? Priority);
