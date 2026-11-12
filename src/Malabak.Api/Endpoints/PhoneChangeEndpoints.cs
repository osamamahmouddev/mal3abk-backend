using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PhoneChange.CancelPhoneChange;
using Malabak.Modules.Identity.Application.PhoneChange.RecoverPhone;
using Malabak.Modules.Identity.Application.PhoneChange.StartPhoneChange;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyNewPhone;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyOldPhone;
using Malabak.Api.Infrastructure.Security;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class PhoneChangeEndpoints
{
    // Wrong-code attempts are capped at 5 per code on the request itself; this only bounds hammering of the verify
    // and cancel endpoints (one legitimate flow needs a handful of calls, so the daily start limit can't apply to them).
    private const int _phoneChangeStepLimit = 20;

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
                    OtpDeliveryErrors.DeliveryFailedCode => StatusCodes.Status503ServiceUnavailable,
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
        // SECURITY.md §5.5 step 1 / §7.5: 3 phone-change requests per user per day.
        .RequireDistributedRateLimiting("phone-change-start", permitLimit: 3, windowSeconds: 86_400, isSensitive: true)
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
                    statusCode: result.Error.Code == OtpDeliveryErrors.DeliveryFailedCode
                        ? StatusCodes.Status503ServiceUnavailable
                        : StatusCodes.Status400BadRequest);
            }

            return Results.Ok(new { message = "Old phone number verified. OTP sent to new phone." });
        })
        .RequireAuthorization()
        .RequireDistributedRateLimiting("phone-change-verify", permitLimit: _phoneChangeStepLimit, windowSeconds: 3_600, isSensitive: true)
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
                return Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Description,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .RequireDistributedRateLimiting("phone-change-verify", permitLimit: _phoneChangeStepLimit, windowSeconds: 3_600, isSensitive: true)
        .WithName("VerifyNewPhone")
        .WithSummary("Verify new phone OTP code in atomic transaction, rotate security stamp and revoke sessions");

        group.MapPost("/cancel", async (
            ClaimsPrincipal user,
            [FromBody] CancelPhoneChangeApiRequest request,
            ICommandHandler<CancelPhoneChangeCommand, Result> handler,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            if (!userId.HasValue)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(new CancelPhoneChangeCommand(userId.Value, request.RequestId), cancellationToken);

            if (result.IsFailure)
            {
                return Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Description,
                    statusCode: result.Error.Code == "Identity.PhoneChangeRequestNotFound"
                        ? StatusCodes.Status404NotFound
                        : StatusCodes.Status400BadRequest);
            }

            return Results.Ok(new { message = "Phone change request cancelled." });
        })
        .RequireAuthorization()
        .RequireDistributedRateLimiting("phone-change-verify", permitLimit: _phoneChangeStepLimit, windowSeconds: 3_600, isSensitive: true)
        .WithName("CancelPhoneChange")
        .WithSummary("Cancel an in-progress phone change request");

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

        // Step-up tokens are issued only by POST /auth/step-up after real re-verification (TASK-209).
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
    public sealed record CancelPhoneChangeApiRequest(Guid RequestId);
    public sealed record RecoverPhoneApiRequest(string RecoveryToken);
}
