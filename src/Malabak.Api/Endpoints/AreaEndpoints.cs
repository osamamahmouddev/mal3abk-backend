using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Venues.Application.Areas;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Areas on top of the built-in catalog: admins manage the list, venue owners can add one for their place.</summary>
public static class AreaEndpoints
{
    public static void MapAreaEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0);

        v1.MapPost("/venues/areas", async ([FromBody] AddAreaApiRequest request, ClaimsPrincipal user, AreaCatalogService service, CancellationToken ct) =>
            {
                var result = await service.AddAsync(request.GovernorateCode, request.NameAr, request.NameEn, user.GetUserId()!.Value, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/reference/governorates", result.Value) : AdminResults.Problem(result.Error);
            })
            .RequireAuthorization(Permissions.VenuesManage)
            .RequireDistributedRateLimiting("venue-add-area", permitLimit: 20, windowSeconds: 3600)
            .Produces<CustomAreaDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Venues")
            .WithName("AddArea").WithSummary("Add an area missing from the catalog so a venue can be listed under it");

        var admin = v1.MapGroup("/admin/areas").RequireAuthorization(Permissions.AdminSettingsManage)
            .AddEndpointFilter<AdminIpAllowlistFilter>().WithTags("Admin Areas");

        admin.MapGet("", async (HttpContext http, AreaCatalogService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return Results.Ok(await service.ListAsync(ct));
            })
            .Produces<IReadOnlyList<CustomAreaDto>>()
            .WithName("AdminListAreas").WithSummary("Areas added on top of the built-in catalog");

        admin.MapPost("", async ([FromBody] AddAreaApiRequest request, ClaimsPrincipal user, AreaCatalogService service, CancellationToken ct) =>
            {
                var result = await service.AddAsync(request.GovernorateCode, request.NameAr, request.NameEn, user.GetUserId()!.Value, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/admin/areas/{result.Value.Id}", result.Value) : AdminResults.Problem(result.Error);
            })
            .Produces<CustomAreaDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminAddArea").WithSummary("Add an area to a governorate");

        admin.MapDelete("/{areaId:guid}", async (Guid areaId, AreaCatalogService service, CancellationToken ct) =>
                AdminResults.From(await service.RemoveAsync(areaId, ct)))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminRemoveArea").WithSummary("Remove an added area (venues already under it keep their text)");
    }
}

public sealed record AddAreaApiRequest(string? GovernorateCode, string? NameAr, string? NameEn);
