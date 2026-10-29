using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Identity.Application.AdminAuth;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class AdminAuthEndpoints
{
    public static void MapAdminAuthEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var auth = app.MapGroup("/api/v{version:apiVersion}/admin/auth")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .AddEndpointFilter<AdminIpAllowlistFilter>()
            .RequireRateLimiting(PasswordAuthEndpoints.RateLimitPolicy)
            .WithTags("Admin Authentication");

        auth.MapPost("/login", async ([FromBody] AdminLoginApiRequest request, HttpContext http,
            ICommandHandler<AdminLoginCommand, Result<AdminLoginResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new AdminLoginCommand(request.Email, request.Password, request.TotpCode,
                request.RecoveryCode, request.TurnstileToken, http.Connection.RemoteIpAddress?.ToString()), ct);
            return NoStore(http, result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error));
        })
        .AllowAnonymous()
        .WithName("AdminLogin").WithSummary("Admin sign-in: password + TOTP or recovery code; first login returns an enrollment token");

        auth.MapPost("/totp/enroll", async ([FromBody] EnrollmentApiRequest request, HttpContext http,
            ICommandHandler<AdminEnrollTotpCommand, Result<TotpEnrollmentResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new AdminEnrollTotpCommand(request.EnrollmentToken), ct);
            return NoStore(http, result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error));
        })
        .AllowAnonymous().WithName("AdminEnrollTotp");

        auth.MapPost("/totp/confirm", async ([FromBody] ConfirmApiRequest request, HttpContext http,
            ICommandHandler<AdminConfirmTotpCommand, Result<IReadOnlyList<string>>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new AdminConfirmTotpCommand(request.EnrollmentToken, request.Code), ct);
            return NoStore(http, result.IsSuccess ? Results.Ok(new { RecoveryCodes = result.Value }) : ToProblem(result.Error));
        })
        .AllowAnonymous().WithName("AdminConfirmTotp").WithSummary("Turn on TOTP; returns one-time recovery codes (shown once)");

        auth.MapPost("/refresh", async ([FromBody] RefreshApiRequest request, HttpContext http,
            ICommandHandler<AdminRefreshCommand, Result<AdminTokens>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new AdminRefreshCommand(request.RefreshToken), ct);
            return NoStore(http, result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error));
        })
        .AllowAnonymous().WithName("AdminRefresh");

        auth.MapPost("/logout", async ([FromBody] RefreshApiRequest request, ICommandHandler<AdminLogoutCommand, Result> handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new AdminLogoutCommand(request.RefreshToken), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(MalabakAuthorization.AdminSessionPolicy).WithName("AdminLogout");

        var sessions = app.MapGroup("/api/v{version:apiVersion}/admin/sessions")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .AddEndpointFilter<AdminIpAllowlistFilter>()
            .RequireAuthorization(Permissions.AdminUsersManage)
            .WithTags("Admin Sessions");

        sessions.MapGet("/", async (IQueryHandler<ListAdminSessionsQuery, Result<IReadOnlyList<AdminSessionDto>>> handler, CancellationToken ct) =>
            Results.Ok((await handler.HandleAsync(new ListAdminSessionsQuery(), ct)).Value))
        .WithName("ListAdminSessions");

        sessions.MapDelete("/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal user,
            ICommandHandler<RevokeAdminSessionCommand, Result> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RevokeAdminSessionCommand(sessionId, user.GetUserId()!.Value), ct);
            return result.IsSuccess ? Results.NoContent() : ToProblem(result.Error);
        })
        .WithName("RevokeAdminSession");
    }

    private static IResult NoStore(HttpContext http, IResult result)
    {
        http.Response.Headers.CacheControl = "no-store";
        return result;
    }

    private static IResult ToProblem(Error error)
    {
        var status = error.Code switch
        {
            "Identity.LoginThrottled" => StatusCodes.Status429TooManyRequests,
            "Identity.SessionNotFound" => StatusCodes.Status404NotFound,
            "Identity.CaptchaRequired" => StatusCodes.Status403Forbidden,
            "Identity.InvalidCredentials" or "Identity.InvalidTotpCode" or "Identity.InvalidRefreshToken" => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(title: error.Code, detail: error.Description, statusCode: status);
    }

    public sealed record AdminLoginApiRequest(string Email, string Password, string? TotpCode, string? RecoveryCode, string? TurnstileToken);

    public sealed record EnrollmentApiRequest(string EnrollmentToken);

    public sealed record ConfirmApiRequest(string EnrollmentToken, string Code);

    public sealed record RefreshApiRequest(string RefreshToken);
}
