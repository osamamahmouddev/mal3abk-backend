using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Crm;
using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>The platform team's CRM for venues: contacts, stages, notes and follow-ups. Venue onboarding staff only.</summary>
public static class AdminCrmEndpoints
{
    public static void MapAdminCrmEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var crm = app.MapGroup("/api/v{version:apiVersion}/admin/crm")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .AddEndpointFilter<AdminIpAllowlistFilter>()
            .RequireAuthorization(Permissions.AdminVenuesApprove)
            .WithTags("Admin CRM");

        crm.MapGet("/summary", async ([FromQuery] string? region, [FromQuery] bool? unassigned, HttpContext http, CrmService service,
                CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return Results.Ok(await service.GetSummaryAsync(new CrmRegionFilter(region, unassigned == true), ct));
            })
            .Produces<CrmSummary>()
            .WithName("AdminCrmSummary")
            .WithSummary("Contacts per stage, follow-ups due today and venue waitlist leads not in the CRM yet; optionally for one region");

        crm.MapGet("/regions", async (HttpContext http, CrmService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return Results.Ok(await service.GetRegionsAsync(ct));
            })
            .Produces<CrmRegions>()
            .WithName("AdminCrmRegions")
            .WithSummary("The regions the board is split by, most contacts first, and how many contacts have no region yet");

        crm.MapGet("/contacts", async ([FromQuery] CrmStage? stage, [FromQuery] string? q, [FromQuery] bool? due, [FromQuery] int? page,
                [FromQuery] int? pageSize, [FromQuery] string? region, [FromQuery] bool? unassigned, HttpContext http, CrmService service,
                CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.ListAsync(stage, q, due == true, page ?? 1, pageSize ?? 50,
                    new CrmRegionFilter(region, unassigned == true), ct));
            })
            .Produces<CrmContactPage>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminCrmListContacts")
            .WithSummary("Contacts, next follow-up first; q matches a phone number exactly or a name, venue or area; region (or unassigned) narrows to one region");

        crm.MapGet("/contacts/{id:guid}", async (Guid id, HttpContext http, CrmService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.GetAsync(id, ct));
            })
            .Produces<CrmContactDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminCrmGetContact")
            .WithSummary("A contact with its timeline (notes, calls and stage changes)");

        crm.MapPost("/contacts", async ([FromBody] CrmContactApiRequest request, ClaimsPrincipal user, CrmService service, CancellationToken ct) =>
                AdminResults.From(await service.CreateAsync(request.ToInput(), user.GetUserId()!.Value, ct)))
            .Produces<CrmContactDetail>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminCrmCreateContact")
            .WithSummary("Add a contact; one per phone number");

        crm.MapPut("/contacts/{id:guid}", async (Guid id, [FromBody] CrmContactApiRequest request, ClaimsPrincipal user, CrmService service,
                CancellationToken ct) =>
                AdminResults.From(await service.UpdateAsync(id, request.ToInput(), user.GetUserId()!.Value, ct)))
            .Produces<CrmContactDetail>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminCrmUpdateContact")
            .WithSummary("Edit a contact's details and follow-up; an empty phone keeps the current one");

        crm.MapPost("/contacts/{id:guid}/stage", async (Guid id, [FromBody] CrmStageApiRequest request, ClaimsPrincipal user, CrmService service,
                CancellationToken ct) =>
                AdminResults.From(await service.MoveAsync(id, request.Stage, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<CrmContactDetail>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminCrmMoveContact")
            .WithSummary("Move a contact to another stage (Lost needs a reason); the move is kept on its timeline");

        crm.MapPost("/contacts/{id:guid}/notes", async (Guid id, [FromBody] CrmNoteApiRequest request, ClaimsPrincipal user, CrmService service,
                CancellationToken ct) =>
                AdminResults.From(await service.AddNoteAsync(id, request.Kind ?? CrmNoteKind.Note, request.Text, user.GetUserId()!.Value, ct)))
            .Produces<CrmContactDetail>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminCrmAddNote")
            .WithSummary("Add a note or log a call");

        crm.MapPost("/import-waitlist", async (ClaimsPrincipal user, CrmService service, CancellationToken ct) =>
                Results.Ok(await service.ImportWaitlistAsync(user.GetUserId()!.Value, ct)))
            .RequireDistributedRateLimiting("admin-crm-import", permitLimit: 5, windowSeconds: 60)
            .Produces<CrmImportResult>()
            .WithName("AdminCrmImportWaitlist")
            .WithSummary("Add the venue owners from the landing page waitlist as new contacts (each once)");
    }
}

public sealed record CrmContactApiRequest(
    string? Name, string? VenueName, string? Area, string? Phone, DateTime? NextFollowUpAtUtc, Guid? AssignedToUserId, CrmSource? Source = null,
    string? Region = null)
{
    public CrmContactInput ToInput() => new(Name, VenueName, Area, Phone, NextFollowUpAtUtc, AssignedToUserId, Source ?? CrmSource.Manual, Region);
}

public sealed record CrmStageApiRequest(CrmStage Stage, string? Reason = null);

public sealed record CrmNoteApiRequest(string? Text, CrmNoteKind? Kind = null);
