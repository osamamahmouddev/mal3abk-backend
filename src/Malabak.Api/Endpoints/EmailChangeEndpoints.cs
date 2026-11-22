using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Identity.Application.EmailChange;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>
/// Changing the email an owner or staff member signs in with: a password step-up starts it, a code sent to the new address
/// confirms it, then every session signs in again with the new address.
/// </summary>
public static class EmailChangeEndpoints
{
    public static void MapEmailChangeEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/me/email-change")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization()
            .WithTags("Email Change");

        group.MapPost("/start", async (ClaimsPrincipal user, [FromBody] StartEmailChangeApiRequest request,
                ICommandHandler<StartEmailChangeCommand, Result> handler, HttpContext http, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return ToResult(await handler.HandleAsync(new StartEmailChangeCommand(user.GetUserId()!.Value, request.NewEmail, request.StepUpToken), ct));
            })
            // Each start emails a code to an address the user typed: 3 a day per user, like phone changes (SECURITY.md §7.5).
            .RequireDistributedRateLimiting("email-change-start", permitLimit: 3, windowSeconds: 86_400, isSensitive: true)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithName("StartEmailChange")
            .WithSummary("Send a code to the new email (requires a change_email step-up token from the last 5 minutes)");

        group.MapPost("/confirm", async (ClaimsPrincipal user, [FromBody] ConfirmEmailChangeApiRequest request,
                ICommandHandler<ConfirmEmailChangeCommand, Result> handler, HttpContext http, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return ToResult(await handler.HandleAsync(new ConfirmEmailChangeCommand(user.GetUserId()!.Value, request.Code), ct));
            })
            .RequireDistributedRateLimiting("email-change-confirm", permitLimit: 10, windowSeconds: 900, isSensitive: true)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("ConfirmEmailChange")
            .WithSummary("Enter the code sent to the new email; the email changes and every session is signed out");
    }

    private static IResult ToResult(Result result)
    {
        if (result.IsSuccess)
        {
            return Results.NoContent();
        }

        var status = result.Error.Code == EmailChangeErrors.InvalidStepUpToken.Code ? StatusCodes.Status401Unauthorized : StatusCodes.Status400BadRequest;
        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }

    public sealed record StartEmailChangeApiRequest(string? NewEmail, string? StepUpToken);

    public sealed record ConfirmEmailChangeApiRequest(string? Code);
}
