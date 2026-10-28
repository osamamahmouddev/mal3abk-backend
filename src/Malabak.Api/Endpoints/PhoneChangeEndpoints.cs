using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PhoneChange.RecoverPhone;
using Malabak.Modules.Identity.Application.PhoneChange.StartPhoneChange;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyNewPhone;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyOldPhone;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class PhoneChangeEndpoints
{
    public static void MapPhoneChangeEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/me/phone-change")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .WithTags("Phone Change");

        group.MapPost("/start", async (
            ClaimsPrincipal user,
            [FromBody] StartPhoneChangeApiRequest request,
            ICommandHandler<StartPhoneChangeCommand, Result<StartPhoneChangeResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new StartPhoneChangeCommand(userId.Value, request.NewPhoneNumber, request.StepUpToken);
            var result = await handler.HandleAsync(command, cancellationToken);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "Identity.InvalidStepUpToken" => StatusCodes.Status401Unauthorized,
                    "Identity.PhoneChangeCooldownActive" => StatusCodes.Status429TooManyRequests,
                    "Identity.PhoneNumberAlreadyInUse" => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Description,
                    statusCode: statusCode);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithName("StartPhoneChange")
        .WithSummary("Start multi-step phone change (requires <= 5 min step-up token)");

        group.MapPost("/verify-old", async (
            ClaimsPrincipal user,
            [FromBody] VerifyOldPhoneApiRequest request,
            ICommandHandler<VerifyOldPhoneCommand, Result> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new VerifyOldPhoneCommand(userId.Value, request.RequestId, request.Code);
            var result = await handler.HandleAsync(command, cancellationToken);

            if (result.IsFailure)
            {
                return Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Description,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(new { message = "Old phone number verified. OTP sent to new phone." });
        })
        .RequireAuthorization()
        .WithName("VerifyOldPhone")
        .WithSummary("Verify old phone OTP code");

        group.MapPost("/verify-new", async (
            ClaimsPrincipal user,
            [FromBody] VerifyNewPhoneApiRequest request,
            ICommandHandler<VerifyNewPhoneCommand, Result<VerifyNewPhoneResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var command = new VerifyNewPhoneCommand(userId.Value, request.RequestId, request.Code);
            var result = await handler.HandleAsync(command, cancellationToken);

            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "Identity.PhoneNumberAlreadyInUse" => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Description,
                    statusCode: statusCode);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithName("VerifyNewPhone")
        .WithSummary("Verify new phone OTP code in atomic transaction, rotate security stamp and revoke sessions");

        group.MapPost("/recover", async (
            [FromBody] RecoverPhoneApiRequest request,
            ICommandHandler<RecoverPhoneCommand, Result> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RecoverPhoneCommand(request.RecoveryToken);
            var result = await handler.HandleAsync(command, cancellationToken);

            if (result.IsFailure)
            {
                return Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Description,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(new { message = "Phone number restored successfully." });
        })
        .AllowAnonymous()
        .WithName("RecoverPhone")
        .WithSummary("Revert unauthorized phone number change using 24-hour recovery link");

        // Helper endpoint for Step-Up Token generation
        app.MapPost("/api/v{version:apiVersion}/me/step-up", (
            ClaimsPrincipal user,
            [FromBody] StepUpApiRequest request,
            IStepUpService stepUpService) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var token = stepUpService.IssueStepUpToken(userId.Value, request.Scope ?? "change_phone");
            return Results.Ok(new { stepUpToken = token, expiresInSeconds = 300 });
        })
        .WithApiVersionSet(apiVersionSet)
        .MapToApiVersion(1, 0)
        .RequireAuthorization()
        .WithTags("Authentication");
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var claimValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        return Guid.TryParse(claimValue, out var guid) ? guid : null;
    }

    public sealed record StartPhoneChangeApiRequest(string NewPhoneNumber, string StepUpToken);
    public sealed record VerifyOldPhoneApiRequest(Guid RequestId, string Code);
    public sealed record VerifyNewPhoneApiRequest(Guid RequestId, string Code);
    public sealed record RecoverPhoneApiRequest(string RecoveryToken);
    public sealed record StepUpApiRequest(string? Scope = null);
}
