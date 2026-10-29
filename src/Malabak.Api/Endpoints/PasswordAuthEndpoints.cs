using System.Security.Claims;
using System.Threading.RateLimiting;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Malabak.Api.Endpoints;

public static class PasswordAuthEndpoints
{
    public const string RateLimitPolicy = "password-auth";

    /// <summary>Per-IP limit for login and account-email endpoints (SECURITY.md §7.5); per-account throttling lives in the domain.</summary>
    public static void AddPasswordAuthRateLimit(this RateLimiterOptions options) =>
        options.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetSlidingWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new SlidingWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(15), SegmentsPerWindow = 5, QueueLimit = 0 }));

    public static void MapPasswordAuthEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/auth")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .WithTags("Password Authentication");

        group.MapPost("/login", async ([FromBody] PasswordLoginApiRequest request, HttpContext http,
            ICommandHandler<PasswordLoginCommand, Result<PasswordLoginResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new PasswordLoginCommand(
                request.Email, request.Password, request.TotpCode, request.TurnstileToken, http.Connection.RemoteIpAddress?.ToString()), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
        })
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicy)
        .WithName("PasswordLogin").WithSummary("Venue owner/staff sign-in with email + password (+ TOTP when enabled)");

        group.MapPost("/register/venue-owner", async ([FromBody] RegisterVenueOwnerApiRequest request,
            ICommandHandler<RegisterVenueOwnerCommand, Result> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new RegisterVenueOwnerCommand(request.Email, request.PhoneNumber, request.FullName, request.Password), ct), StatusCodes.Status202Accepted))
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicy)
        .WithName("RegisterVenueOwner").WithSummary("Create a venue owner account; a verification email is sent");

        group.MapPost("/email/verify", async ([FromBody] TokenApiRequest request, ICommandHandler<VerifyEmailCommand, Result> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new VerifyEmailCommand(request.Token), ct)))
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicy).WithName("VerifyEmail");

        group.MapPost("/email/resend", async ([FromBody] EmailApiRequest request, ICommandHandler<ResendVerificationCommand, Result> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new ResendVerificationCommand(request.Email), ct), StatusCodes.Status202Accepted))
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicy).WithName("ResendVerificationEmail");

        group.MapPost("/password/forgot", async ([FromBody] EmailApiRequest request, ICommandHandler<ForgotPasswordCommand, Result> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new ForgotPasswordCommand(request.Email), ct), StatusCodes.Status202Accepted))
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicy)
        .WithName("ForgotPassword").WithSummary("Always 202: if the email has an account, a reset link is sent");

        group.MapPost("/password/reset", async ([FromBody] ResetPasswordApiRequest request, ICommandHandler<ResetPasswordCommand, Result> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new ResetPasswordCommand(request.Token, request.NewPassword), ct)))
        .AllowAnonymous().RequireRateLimiting(RateLimitPolicy).WithName("ResetPassword");

        group.MapPost("/password/change", async ([FromBody] ChangePasswordApiRequest request, ClaimsPrincipal user,
            ICommandHandler<ChangePasswordCommand, Result> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new ChangePasswordCommand(user.GetUserId()!.Value, request.CurrentPassword, request.NewPassword), ct)))
        .RequireAuthorization().RequireRateLimiting(RateLimitPolicy)
        .WithName("ChangePassword").WithSummary("Change password; signs out all other sessions");

        group.MapPost("/2fa/totp/enroll", async (ClaimsPrincipal user, ICommandHandler<EnrollTotpCommand, Result<TotpEnrollmentResponse>> handler,
            HttpContext http, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new EnrollTotpCommand(user.GetUserId()!.Value), ct);
            http.Response.Headers.CacheControl = "no-store";
            return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
        })
        .RequireAuthorization(Malabak.SharedKernel.Authorization.Permissions.VenuesManage)
        .WithName("EnrollTotp").WithSummary("Start TOTP enrollment (secret shown once)");

        group.MapPost("/2fa/totp/confirm", async ([FromBody] TotpCodeApiRequest request, ClaimsPrincipal user,
            ICommandHandler<ConfirmTotpCommand, Result> handler, CancellationToken ct) =>
            ToResult(await handler.HandleAsync(new ConfirmTotpCommand(user.GetUserId()!.Value, request.Code), ct)))
        .RequireAuthorization(Malabak.SharedKernel.Authorization.Permissions.VenuesManage).RequireRateLimiting(RateLimitPolicy)
        .WithName("ConfirmTotp").WithSummary("Confirm a code from the authenticator app to turn on 2FA");
    }

    private static IResult ToResult(Result result, int successStatus = StatusCodes.Status204NoContent) =>
        result.IsSuccess ? Results.StatusCode(successStatus) : ToProblem(result.Error);

    private static IResult ToProblem(Error error)
    {
        var status = error.Code switch
        {
            "Identity.LoginThrottled" => StatusCodes.Status429TooManyRequests,
            "Identity.InvalidCredentials" or "Identity.InvalidTotpCode" => StatusCodes.Status401Unauthorized,
            "Identity.EmailNotVerified" or "Identity.TwoFactorRequired" or "Identity.CaptchaRequired" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(title: error.Code, detail: error.Description, statusCode: status);
    }

    public sealed record PasswordLoginApiRequest(string Email, string Password, string? TotpCode, string? TurnstileToken);

    public sealed record RegisterVenueOwnerApiRequest(string Email, string PhoneNumber, string FullName, string Password);

    public sealed record TokenApiRequest(string Token);

    public sealed record EmailApiRequest(string Email);

    public sealed record ResetPasswordApiRequest(string Token, string NewPassword);

    public sealed record ChangePasswordApiRequest(string CurrentPassword, string NewPassword);

    public sealed record TotpCodeApiRequest(string Code);
}
