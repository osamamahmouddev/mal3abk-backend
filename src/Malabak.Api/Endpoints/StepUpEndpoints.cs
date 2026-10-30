using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.StepUp;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class StepUpEndpoints
{
    public static void MapStepUpEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/auth/step-up")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization(MalabakAuthorization.AnySessionPolicy)
            .RequireRateLimiting(PasswordAuthEndpoints.RateLimitPolicy)
            .WithTags("Step-up Verification");

        group.MapPost("/otp", async (ClaimsPrincipal user, ICommandHandler<SendStepUpOtpCommand, Result> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new SendStepUpOtpCommand(user.GetUserId()!.Value), ct);
            return result.IsSuccess ? Results.Accepted() : ToProblem(result.Error);
        })
        .WithName("SendStepUpOtp").WithSummary("Send a step-up code to the account's current phone");

        group.MapPost("/", async ([FromBody] StepUpApiRequest request, ClaimsPrincipal user, HttpContext http,
            ICommandHandler<VerifyStepUpCommand, Result<StepUpTokenResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new VerifyStepUpCommand(user.GetUserId()!.Value, request.Scope, request.Method, request.Secret), ct);
            http.Response.Headers.CacheControl = "no-store";
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
        })
        .WithName("VerifyStepUp").WithSummary("Re-verify (password, TOTP or OTP) and get a 5-minute token for one sensitive scope");
    }

    private static IResult ToProblem(Error error)
    {
        var status = error.Code switch
        {
            "Identity.LoginThrottled" or "Identity.OtpRateLimitExceeded" => StatusCodes.Status429TooManyRequests,
            "Identity.StepUpFailed" => StatusCodes.Status401Unauthorized,
            OtpDeliveryErrors.DeliveryFailedCode => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(title: error.Code, detail: error.Description, statusCode: status);
    }

    public sealed record StepUpApiRequest(string Scope, StepUpMethod Method, string Secret);
}
