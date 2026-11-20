using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Identity.Application.Admin;
using Malabak.Modules.Identity.Application.Authorization;
using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>
/// Admin user management (TASK-701), staff accounts and system roles (TASK-713) and role-based navigation for the admin app.
/// Destructive actions need a reason and a fresh step-up (TOTP) confirmation, and are all audited.
/// </summary>
public static class AdminUserEndpoints
{
    public const int HistoryItems = 20;

    public static void MapAdminUserEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        MapUsers(app, apiVersionSet);
        MapAccounts(app, apiVersionSet);
    }

    private static void MapUsers(IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var users = app.MapGroup("/api/v{version:apiVersion}/admin/users")
            .WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.AdminUsersView)
            .WithTags("Admin Users");

        users.MapGet("", async ([FromQuery] string? q, [FromQuery] string? role, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize,
                HttpContext http, AdminUserService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.SearchAsync(new AdminUserSearch(q, role, status, page ?? 1, pageSize ?? 25), ct));
            })
            .Produces<AdminUserPage>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminSearchUsers").WithSummary("Search users by name, phone, email or id, with role and status filters (phones masked)");

        users.MapGet("/{userId:guid}", async (Guid userId, ClaimsPrincipal user, HttpContext http, AdminUserService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return AdminResults.From(await service.GetAsync(userId, user.GetUserId()!.Value, ct));
            })
            .Produces<AdminUserDetail>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("AdminGetUser").WithSummary("User profile, status and sessions (the view is audited)");

        users.MapGet("/{userId:guid}/history", async (Guid userId, HttpContext http, IEnumerable<IUserActivityProvider> providers, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                var summaries = new List<UserActivitySummary>();
                foreach (var provider in providers)
                {
                    summaries.Add(await provider.GetAsync(userId, HistoryItems, ct));
                }

                return Results.Ok(summaries.OrderBy(s => s.Source, StringComparer.Ordinal).ToList());
            })
            .Produces<IReadOnlyList<UserActivitySummary>>()
            .WithName("AdminGetUserHistory").WithSummary("Booking and community history (teams, fair-play reports filed and received)");

        users.MapPost("/{userId:guid}/suspend", async (Guid userId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminUserService service, CancellationToken ct) =>
                AdminResults.From(await service.SuspendAsync(userId, request.Reason, user.GetUserId()!.Value, ct)))
            .RequireAuthorization(Permissions.AdminUsersManage)
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces<AdminUserDetail>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminSuspendUser").WithSummary("Suspend a user (reason + step-up required); signs them out everywhere");

        users.MapPost("/{userId:guid}/reactivate", async (Guid userId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminUserService service, CancellationToken ct) =>
                AdminResults.From(await service.ReactivateAsync(userId, request.Reason, user.GetUserId()!.Value, ct)))
            .RequireAuthorization(Permissions.AdminUsersManage)
            .Produces<AdminUserDetail>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminReactivateUser").WithSummary("Lift a suspension (reason required)");

        users.MapPost("/{userId:guid}/sessions/revoke", async (Guid userId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminUserService service, CancellationToken ct) =>
                AdminResults.From(await service.RevokeSessionsAsync(userId, request.Reason, user.GetUserId()!.Value, ct)))
            .RequireAuthorization(Permissions.AdminUsersManage)
            .Produces<int>().ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("AdminRevokeUserSessions").WithSummary("Support action: sign the user out of every device (returns the number of sessions revoked)");
    }

    private static void MapAccounts(IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var accounts = app.MapGroup("/api/v{version:apiVersion}/admin/accounts")
            .WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.AdminAccountsManage)
            .WithTags("Admin Accounts");

        accounts.MapGet("", async (HttpContext http, AdminAccountService service, CancellationToken ct) =>
            {
                AdminResults.NoStore(http);
                return Results.Ok(await service.ListAsync(ct));
            })
            .Produces<IReadOnlyList<AdminAccountDto>>().WithName("AdminListAccounts").WithSummary("Platform staff accounts (Admin, SupportAgent, Finance)");

        accounts.MapPost("", async ([FromBody] CreateAdminAccountApiRequest request, ClaimsPrincipal user, AdminAccountService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(new CreateAdminAccountInput(request.FullName, request.Email, request.PhoneNumber, request.Roles), user.GetUserId()!.Value, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/admin/accounts/{result.Value.Id}", result.Value) : AdminResults.Problem(result.Error);
            })
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces<AdminAccountDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminCreateAccount").WithSummary("Create a staff account; the person gets a set-password link and must enrol TOTP");

        accounts.MapPut("/{accountId:guid}/roles", async (Guid accountId, [FromBody] ReplaceRolesApiRequest request, ClaimsPrincipal user, AdminAccountService service, CancellationToken ct) =>
                AdminResults.From(await service.ReplaceRolesAsync(accountId, request.Roles, request.Reason, user.GetUserId()!.Value, ct)))
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces<AdminAccountDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminReplaceAccountRoles").WithSummary("Replace a staff member's roles (the last active Admin cannot be removed)");

        accounts.MapPost("/{accountId:guid}/disable", async (Guid accountId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminAccountService service, CancellationToken ct) =>
                AdminResults.From(await service.DisableAsync(accountId, request.Reason, user.GetUserId()!.Value, ct)))
            .RequireStepUp(StepUpScopes.AdminDestructive)
            .Produces<AdminAccountDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminDisableAccount").WithSummary("Disable a staff account and revoke its sessions");

        accounts.MapPost("/{accountId:guid}/enable", async (Guid accountId, [FromBody] AdminReasonApiRequest request, ClaimsPrincipal user, AdminAccountService service, CancellationToken ct) =>
                AdminResults.From(await service.EnableAsync(accountId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<AdminAccountDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AdminEnableAccount").WithSummary("Re-enable a disabled staff account");

        app.MapGet("/api/v{version:apiVersion}/admin/roles", () => Results.Ok(AdminAccountService.Roles()))
            .WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0)
            .RequireAuthorization(Permissions.AdminAccountsManage)
            .Produces<IReadOnlyList<SystemRoleDto>>().WithTags("Admin Accounts")
            .WithName("AdminListRoles").WithSummary("System roles and the permissions each grants");

        app.MapGet("/api/v{version:apiVersion}/admin/navigation", async (ClaimsPrincipal user, IUserPermissionService permissions, CancellationToken ct) =>
                Results.Ok(AdminNavigation.For(await permissions.GetAsync(user.GetUserId()!.Value, ct))))
            .WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0)
            .RequireAuthorization(MalabakAuthorization.AdminSessionPolicy)
            .Produces<IReadOnlyList<AdminNavItem>>().WithTags("Admin Accounts")
            .WithName("AdminNavigation").WithSummary("Admin app menu items the signed-in staff member may open (UI hint only; the API still checks every call)");
    }
}

public sealed record AdminNavItem(string Key, string Path, string Permission);

/// <summary>Admin app menu filtered by the caller's current permissions (TASK-713). A UI convenience only: endpoints enforce access.</summary>
public static class AdminNavigation
{
    public static readonly IReadOnlyList<AdminNavItem> All =
    [
        new("dashboard", "/", Permissions.AdminBookingsView),
        new("users", "/users", Permissions.AdminUsersView),
        new("venues", "/venues", Permissions.AdminVenuesApprove),
        new("approvals", "/approvals", Permissions.AdminVenuesApprove),
        new("crm", "/crm", Permissions.AdminVenuesApprove),
        new("bookings", "/bookings", Permissions.AdminBookingsView),
        new("disputes", "/disputes", Permissions.AdminDisputesResolve),
        new("moderation", "/moderation", Permissions.AdminDisputesResolve),
        new("support", "/support", Permissions.SupportTicketsManage),
        new("deposits", "/deposits", Permissions.AdminPaymentsManage),
        new("payouts", "/payouts", Permissions.AdminPaymentsManage),
        new("invoices", "/invoices", Permissions.InvoicesView),
        new("featured", "/featured", Permissions.AdminSettingsManage),
        new("settings", "/settings", Permissions.AdminSettingsManage),
        new("accounts", "/accounts", Permissions.AdminAccountsManage),
        new("audit", "/audit", Permissions.AuditLogView)
    ];

    public static IReadOnlyList<AdminNavItem> For(UserPermissions? permissions) =>
        permissions is null ? [] : All.Where(item => permissions.Has(item.Permission)).ToList();
}

public sealed record AdminReasonApiRequest(string? Reason);

public sealed record CreateAdminAccountApiRequest(string? FullName, string? Email, string? PhoneNumber, IReadOnlyList<string>? Roles);

public sealed record ReplaceRolesApiRequest(IReadOnlyList<string>? Roles, string? Reason);
