using Malabak.Modules.Identity.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Malabak.Api.Infrastructure.Authorization;

/// <summary>
/// Grants a permission policy from the user's current roles in the database rather than the token's
/// role claims, so revoking a role takes effect immediately instead of when the access token expires.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IUserPermissionService _permissionService;

    public PermissionAuthorizationHandler(IUserPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var userId = context.User.GetUserId();
        if (userId is null)
        {
            return;
        }

        var permissions = await _permissionService.GetAsync(userId.Value);
        if (permissions?.Has(requirement.Permission) == true)
        {
            context.Succeed(requirement);
        }
    }
}
