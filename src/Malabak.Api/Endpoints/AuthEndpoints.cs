using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Modules.Identity.Application.Authentication.Logout;
using Malabak.Modules.Identity.Application.Authentication.LogoutAll;
using Malabak.Modules.Identity.Application.Authentication.RefreshToken;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Common;
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
                    statusCode: result.Error.Code switch
                    {
                        OtpDeliveryErrors.DeliveryFailedCode => StatusCodes.Status503ServiceUnavailable,
                        var code when code.Contains("RateLimit") => StatusCodes.Status429TooManyRequests,
                        _ => StatusCodes.Status400BadRequest,
                    });
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

        group.MapPost("/refresh", async (
            [FromBody] RefreshTokenRequest request,
            ICommandHandler<RefreshTokenCommand, Result<RefreshTokenResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new RefreshTokenCommand(request.RefreshToken);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                var statusCode = result.Error.Code switch
                {
                    "Identity.RefreshTokenReused" => StatusCodes.Status401Unauthorized,
                    "Identity.RefreshTokenExpired" => StatusCodes.Status401Unauthorized,
                    "Identity.InvalidRefreshToken" => StatusCodes.Status401Unauthorized,
                    _ => StatusCodes.Status400BadRequest
                };

                return Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Description,
                    statusCode: statusCode);
            }

            return Results.Ok(result.Value);
        })
        .AllowAnonymous()
        .WithName("RefreshToken")
        .WithSummary("Rotate refresh token and issue new ES256 access token");

        group.MapPost("/logout", async (
            [FromBody] LogoutRequest request,
            ICommandHandler<LogoutCommand, Result> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new LogoutCommand(request.RefreshToken);
            await handler.HandleAsync(command, cancellationToken);
            return Results.NoContent();
        })
        .AllowAnonymous()
        .WithName("Logout")
        .WithSummary("Revoke refresh token and log out current session");

        group.MapPost("/logout-all", async (
            ClaimsPrincipal user,
            ICommandHandler<LogoutAllCommand, Result> handler,
            CancellationToken cancellationToken) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;

            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var command = new LogoutAllCommand(userId);
            await handler.HandleAsync(command, cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("LogoutAll")
        .WithSummary("Revoke all active sessions and rotate security stamp for authenticated user");
    }

    public sealed record SendOtpRequest(string PhoneNumber, string TurnstileToken);
    public sealed record VerifyOtpRequest(string PhoneNumber, string Code, string? FullName = null);
    public sealed record RefreshTokenRequest(string RefreshToken);
    public sealed record LogoutRequest(string RefreshToken);
}
