using System.Security.Claims;
using System.Text.Json;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Privacy;
using Malabak.Modules.Identity.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Account deletion, consent records and personal data export for the signed-in user.</summary>
public static class PrivacyEndpoints
{
    private static readonly JsonSerializerOptions _exportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void MapPrivacyEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var me = app.MapGroup("/api/v{version:apiVersion}/me")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization()
            .WithTags("Privacy");

        me.MapDelete("", async (ClaimsPrincipal user, PrivacyHandlers privacy, CancellationToken ct) =>
        {
            var result = await privacy.DeleteAccountAsync(user.GetUserId()!.Value, ct);
            return result.IsSuccess
                ? Results.Accepted(value: result.Value)
                : Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status404NotFound);
        })
        .RequireStepUp(StepUpScopes.DeleteAccount)
        .WithName("DeleteMyAccount").WithSummary("Soft-delete the account (needs an X-Step-Up-Token for delete_account)");

        me.MapGet("/consents", async (ClaimsPrincipal user, PrivacyHandlers privacy, CancellationToken ct) =>
            Results.Ok(await privacy.GetConsentsAsync(user.GetUserId()!.Value, ct)))
        .WithName("GetMyConsents");

        me.MapPost("/consents", async (ClaimsPrincipal user, [FromBody] ConsentApiRequest request, PrivacyHandlers privacy, CancellationToken ct) =>
        {
            var result = await privacy.RecordConsentAsync(user.GetUserId()!.Value, request.Type, request.Granted, request.Version, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("RecordConsent").WithSummary("Grant or withdraw terms/privacy/notifications/location/marketing consent");

        me.MapGet("/data-export", async (ClaimsPrincipal user, HttpContext http, PrivacyHandlers privacy, CancellationToken ct) =>
        {
            var result = await privacy.ExportAsync(user.GetUserId()!.Value, ct);
            if (result.IsFailure)
            {
                return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: StatusCodes.Status404NotFound);
            }

            http.Response.Headers.CacheControl = "no-store";
            return Results.File(JsonSerializer.SerializeToUtf8Bytes(result.Value, _exportJson), "application/json", "malabak-personal-data.json");
        })
        .RequireStepUp(StepUpScopes.ExportData)
        .WithName("ExportMyData").WithSummary("Download all personal data we hold (needs an X-Step-Up-Token for export_data)");
    }

    public sealed record ConsentApiRequest(ConsentType Type, bool Granted, string? Version);
}
