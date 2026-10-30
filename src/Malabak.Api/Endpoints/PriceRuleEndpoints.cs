using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Venues.Application.Pricing;
using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Venue price rules (TASK-219): CRUD with overlap validation and a price preview for a slot.</summary>
public static class PriceRuleEndpoints
{
    public static void MapPriceRuleEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/venue-manager/venues/{venueId:guid}/price-rules")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.VenuesManage)
            .AddEndpointFilter<VenueTenantEndpointFilter>()
            .WithTags("Venue Pricing");

        group.MapGet("/", async (Guid venueId, ClaimsPrincipal user, PriceRuleManagement rules, CancellationToken ct) =>
            ToResult(await rules.ListAsync(venueId, user.GetUserId()!.Value, ct)))
        .WithName("GetPriceRules").WithSummary("All price rules of the venue (base, peak, weekend, holiday, special date, discount)");

        group.MapPost("/", async (Guid venueId, ClaimsPrincipal user, [FromBody] PriceRuleApiRequest request, PriceRuleManagement rules, CancellationToken ct) =>
        {
            var result = await rules.CreateAsync(venueId, user.GetUserId()!.Value, request.ToTerms(), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/venue-manager/venues/{venueId}/price-rules/{result.Value.Id}", result.Value)
                : Problem(result.Error);
        })
        .WithName("CreatePriceRule");

        group.MapPut("/{ruleId:guid}", async (Guid venueId, Guid ruleId, ClaimsPrincipal user, [FromBody] PriceRuleApiRequest request,
            PriceRuleManagement rules, CancellationToken ct) =>
            ToResult(await rules.UpdateAsync(venueId, user.GetUserId()!.Value, ruleId, request.ToTerms(), request.IsActive ?? true, ct)))
        .WithName("UpdatePriceRule");

        group.MapDelete("/{ruleId:guid}", async (Guid venueId, Guid ruleId, ClaimsPrincipal user, PriceRuleManagement rules, CancellationToken ct) =>
        {
            var result = await rules.DeleteAsync(venueId, user.GetUserId()!.Value, ruleId, ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result.Error);
        })
        .WithName("DeletePriceRule");

        group.MapPost("/preview", async (Guid venueId, ClaimsPrincipal user, [FromBody] PricePreviewApiRequest request, PriceRuleManagement rules, CancellationToken ct) =>
            ToResult(await rules.PreviewAsync(venueId, user.GetUserId()!.Value, request.PitchId, request.StartUtc, request.EndUtc, request.Draft?.ToTerms(), ct)))
        .WithName("PreviewPrice").WithSummary("Price a slot with the saved rules, and optionally with a draft rule to compare");
    }

    private static IResult ToResult<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);

    private static IResult Problem(Error error)
    {
        var status = StatusCodes.Status400BadRequest;
        if (error.Code.EndsWith("NotFound", StringComparison.Ordinal))
        {
            status = StatusCodes.Status404NotFound;
        }
        else if (error == PriceRuleErrors.Overlap)
        {
            status = StatusCodes.Status409Conflict;
        }

        return Results.Problem(title: error.Code, detail: error.Description, statusCode: status);
    }

    public sealed record PriceRuleApiRequest(
        PriceRuleKind Kind,
        PriceAdjustment Adjustment,
        Guid? PitchId,
        string Name,
        decimal? Amount,
        decimal? Percent,
        IReadOnlyList<DayOfWeek>? Days,
        TimeOnly? StartTime,
        TimeOnly? EndTime,
        DateOnly? StartDate,
        DateOnly? EndDate,
        int? MinDurationMinutes,
        bool? IsActive)
    {
        public PriceRuleTerms ToTerms()
        {
            DaysOfWeekMask? mask = Days is { Count: > 0 }
                ? Days.Aggregate(DaysOfWeekMask.None, (m, d) => m | (DaysOfWeekMask)(1 << (int)d))
                : null;
            return new PriceRuleTerms(Kind, Adjustment, PitchId, Name ?? string.Empty, Amount, Percent, mask, StartTime, EndTime, StartDate, EndDate, MinDurationMinutes);
        }
    }

    public sealed record PricePreviewApiRequest(Guid PitchId, DateTime StartUtc, DateTime EndUtc, PriceRuleApiRequest? Draft);
}
