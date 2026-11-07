using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Identity.Application.Admin;
using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>Support impersonation (TASK-063): a staff member gets a 60 s single-use code, which is exchanged once for a restricted token.</summary>
public static class ImpersonationEndpoints
{
    public static void MapImpersonationEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0).WithTags("Impersonation");

        v1.MapPost("/admin/impersonate", async ([FromBody] ImpersonateApiRequest request, ClaimsPrincipal user, HttpContext http, ImpersonationService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.IssueCodeAsync(user.GetUserId()!.Value, request.UserId, request.Reason, ct));
            })
            .RequireAuthorization(Permissions.AdminImpersonate)
            .AddEndpointFilter<AdminIpAllowlistFilter>()
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .RequireDistributedRateLimiting("admin-impersonate", permitLimit: 10, windowSeconds: 3600)
            .Produces<ImpersonationCodeDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("AdminImpersonate")
            .WithSummary("Get a 60-second single-use code to sign in as a customer (reason + step-up required; audited, customer notified)");

        v1.MapPost("/auth/impersonation/exchange", async ([FromBody] ImpersonationExchangeApiRequest request, HttpContext http, ImpersonationService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.ExchangeAsync(request.Code, ct));
            })
            .AllowAnonymous()
            .RequireDistributedRateLimiting("impersonation-exchange", permitLimit: 10, windowSeconds: 60)
            .Produces<ImpersonationTokenDto>().ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithName("ExchangeImpersonationCode")
            .WithSummary("Exchange the code once for a 15-minute access token (act claim, no refresh, sensitive actions blocked)");
    }
}

public sealed record ImpersonateApiRequest(Guid UserId, string? Reason);

public sealed record ImpersonationExchangeApiRequest(string? Code);
