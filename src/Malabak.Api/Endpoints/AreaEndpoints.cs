using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Venues.Application.Areas;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Areas across Egyptian governorates: admins manage the catalog, status and focus zones; venue owners can submit missing ones.</summary>
public static class AreaEndpoints
{
    public static void MapAreaEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0);

        v1.MapPost("/venues/areas", async ([FromBody] AddAreaApiRequest request, ClaimsPrincipal user, AreaCatalogService service, CancellationToken ct) =>
            {
                var result = await service.AddAsync(request.GovernorateCode, request.NameAr, request.NameEn, request.IsFocus ?? false, user.GetUserId()!.Value, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/reference/governorates", result.Value) : AdminResults.Problem(result.Error);
            })
            .RequireAuthorization(Permissions.VenuesManage)
            .RequireDistributedRateLimiting("venue-add-area", permitLimit: 20, windowSeconds: 3600)
            .Produces<CustomAreaDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Venues")
            .WithName("AddArea").WithSummary("Add an area missing from the catalog so a venue can be listed under it");

        var admin = v1.MapGroup("/admin/areas").RequireAuthorization(Permissions.AdminSettingsManage)
            .AddEndpointFilter<AdminIpAllowlistFilter>().WithTags("Admin Areas");

        admin.MapGet("", async (
            [FromQuery] string? governorate,
            [FromQuery] string? search,
            [FromQuery] string? status,
            HttpContext http,
            AreaCatalogService service,
            CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return Results.Ok(await service.ListAdminAreasAsync(governorate, search, status, ct));
            })
            .Produces<IReadOnlyList<CustomAreaDto>>()
            .WithName("AdminListAreas").WithSummary("List all areas across governorates with status, focus and filters");

        admin.MapPost("", async ([FromBody] AddAreaApiRequest request, ClaimsPrincipal user, AreaCatalogService service, CancellationToken ct) =>
            {
                var result = await service.AddAsync(request.GovernorateCode, request.NameAr, request.NameEn, request.IsFocus ?? false, user.GetUserId()!.Value, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/admin/areas/{result.Value.Id}", result.Value) : AdminResults.Problem(result.Error);
            })
            .Produces<CustomAreaDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminAddArea").WithSummary("Add an area to a governorate");

        admin.MapPut("/{areaId:guid}/status", async (Guid areaId, [FromBody] SetAreaStatusApiRequest request, AreaCatalogService service, CancellationToken ct) =>
            {
                var result = await service.SetActiveAsync(areaId, request.IsActive, ct);
                return result.IsSuccess ? Results.NoContent() : AdminResults.Problem(result.Error);
            })
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminSetAreaStatus").WithSummary("Activate or pause an area");

        admin.MapPut("/{areaId:guid}/focus", async (Guid areaId, [FromBody] SetAreaFocusApiRequest request, AreaCatalogService service, CancellationToken ct) =>
            {
                var result = await service.SetFocusAsync(areaId, request.IsFocus, ct);
                return result.IsSuccess ? Results.NoContent() : AdminResults.Problem(result.Error);
            })
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminSetAreaFocus").WithSummary("Set or unset area operational focus zone");

        admin.MapDelete("/{areaId:guid}", async (Guid areaId, AreaCatalogService service, CancellationToken ct) =>
                AdminResults.From(await service.RemoveAsync(areaId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminRemoveArea").WithSummary("Remove an area");
    }
}

public sealed record AddAreaApiRequest(string? GovernorateCode, string? NameAr, string? NameEn, bool? IsFocus = false);
public sealed record SetAreaStatusApiRequest(bool IsActive);
public sealed record SetAreaFocusApiRequest(bool IsFocus);
