using Asp.Versioning.Builder;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/auth")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .WithTags("Authentication");

        group.MapPost("/otp/send", async (
            [FromBody] SendOtpRequest request,
            HttpContext httpContext,
            ICommandHandler<SendOtpCommand, Result<SendOtpResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
            var command = new SendOtpCommand(request.PhoneNumber, request.TurnstileToken, remoteIp);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Description,
                    statusCode: result.Error.Code.Contains("RateLimit") ? StatusCodes.Status429TooManyRequests : StatusCodes.Status400BadRequest);
            }

            return Results.Ok(result.Value);
        })
        .AllowAnonymous()
        .WithName("SendOtp")
        .WithSummary("Send 6-digit OTP code to Egyptian phone number");

        group.MapPost("/otp/verify", async (
            [FromBody] VerifyOtpRequest request,
            ICommandHandler<VerifyOtpCommand, Result<VerifyOtpResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new VerifyOtpCommand(request.PhoneNumber, request.Code, request.FullName);

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
        .AllowAnonymous()
        .WithName("VerifyOtp")
        .WithSummary("Verify OTP code and authenticate player");
    }

    public sealed record SendOtpRequest(string PhoneNumber, string TurnstileToken);
    public sealed record VerifyOtpRequest(string PhoneNumber, string Code, string? FullName = null);
}
