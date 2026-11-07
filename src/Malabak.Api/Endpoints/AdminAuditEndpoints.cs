using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Audit.Infrastructure;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Audit log viewer for admins (TASK-062). Read-only: the trail cannot be edited through any endpoint.</summary>
public static class AdminAuditEndpoints
{
    public static void MapAdminAuditEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        app.MapGet("/api/v{version:apiVersion}/admin/audit-logs", async (
            [FromQuery] Guid? actorId,
            [FromQuery] string? action,
            [FromQuery] string? entityType,
            [FromQuery] string? entityId,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext http,
            AuditLogQuery query,
            CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await query.SearchAsync(new AuditLogFilter(actorId, action, entityType, entityId, fromUtc, toUtc, page ?? 1, pageSize ?? 50), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithApiVersionSet(apiVersionSet)
        .MapToApiVersion(1, 0)
        .RequireAuthorization(Permissions.AuditLogView)
        .RequireDistributedRateLimiting("admin-audit-logs", permitLimit: 60, windowSeconds: 60)
        .Produces<AuditLogPage>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithTags("Admin Audit")
        .WithName("SearchAuditLogs")
        .WithSummary("Audit trail, newest first, filtered by actor (or impersonator), action prefix, entity and time range");
    }
}
