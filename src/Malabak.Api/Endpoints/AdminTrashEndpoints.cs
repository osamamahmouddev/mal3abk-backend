using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Platform;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>
/// Delete users, venues, teams and CRM contacts from the admin console, and the trash ("المحذوف مؤخرا") to restore or purge them.
/// Admins only; deleting and purging need step-up, and anything irreversible needs the item's name typed.
/// </summary>
public static class AdminTrashEndpoints
{
    public static void MapAdminTrashEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var trash = app.MapGroup("/api/v{version:apiVersion}/admin/trash")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .AddEndpointFilter<AdminIpAllowlistFilter>()
            .RequireAuthorization(Permissions.AdminUsersManage)
            .WithTags("Admin Trash");

        trash.MapGet("", async ([FromQuery] string? kind, HttpContext http, TrashService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return Results.Ok(await service.ListAsync(kind, ct));
            })
            .Produces<IReadOnlyList<TrashItemDto>>()
            .WithName("AdminListTrash")
            .WithSummary("Items deleted from the admin console, newest first, with the date each is purged automatically");

        trash.MapPost("/items/{kind}/{id:guid}", async (string kind, Guid id, [FromBody] TrashDeleteApiRequest request, ClaimsPrincipal user,
                TrashService service, CancellationToken ct) =>
                AdminResults.From(await service.MoveToTrashAsync(kind, id, request.Reason, request.ConfirmName, user.GetUserId()!.Value, ct)))
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces<TrashItemDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminMoveToTrash")
            .WithSummary("Delete a user, venue, team or CRM contact (kind: user, venue, team, crm); a venue needs its name typed");

        trash.MapPost("/{entryId:guid}/restore", async (Guid entryId, ClaimsPrincipal user, TrashService service, CancellationToken ct) =>
                AdminResults.From(await service.RestoreAsync(entryId, user.GetUserId()!.Value, ct)))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminRestoreFromTrash")
            .WithSummary("Put an item back where it was");

        trash.MapPost("/{entryId:guid}/purge", async (Guid entryId, [FromBody] TrashPurgeApiRequest request, ClaimsPrincipal user,
                TrashService service, CancellationToken ct) =>
                AdminResults.From(await service.PurgeAsync(entryId, request.ConfirmName, user.GetUserId()!.Value, ct)))
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminPurgeFromTrash")
            .WithSummary("Erase an item for good (its name typed to confirm); accounting records stay without personal data");

        trash.MapPost("/purge-all", async ([FromBody] TrashPurgeAllApiRequest request, ClaimsPrincipal user, TrashService service, CancellationToken ct) =>
                AdminResults.From(await service.PurgeAllAsync(request.Confirmation, user.GetUserId()!.Value, ct)))
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .RequireDistributedRateLimiting("admin-trash-purge-all", permitLimit: 3, windowSeconds: 60)
            .Produces<TrashPurgeAllResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminEmptyTrash")
            .WithSummary("Erase everything in the trash for good (confirmation: «مسح الكل»)");
    }
}

public sealed record TrashDeleteApiRequest(string? Reason, string? ConfirmName = null);

public sealed record TrashPurgeApiRequest(string? ConfirmName);

public sealed record TrashPurgeAllApiRequest(string? Confirmation);
