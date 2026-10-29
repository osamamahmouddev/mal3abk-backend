using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Malabak.Api.Infrastructure.Authorization;

public static class MalabakAuthorization
{
    /// <summary>JWT scheme that only accepts admin-audience tokens.</summary>
    public const string AdminScheme = "AdminBearer";

    /// <summary>Policy for admin endpoints that need an admin session but no specific permission.</summary>
    public const string AdminSessionPolicy = "admin.session";

    /// <summary>Policy accepting either a regular or an admin session.</summary>
    public const string AnySessionPolicy = "any.session";

    /// <summary>
    /// Deny by default (every endpoint needs an authenticated user unless marked AllowAnonymous) plus one
    /// policy per platform permission, named after the permission (e.g. <c>RequireAuthorization(Permissions.VenuesManage)</c>).
    /// Platform-staff permissions authenticate only with the admin scheme, so a normal token can never satisfy them.
    /// </summary>
    public static IServiceCollection AddMalabakAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(AdminSessionPolicy, policy => policy
                .AddAuthenticationSchemes(AdminScheme)
                .RequireAuthenticatedUser());

            // Endpoints shared by all signed-in users, including admins (e.g. step-up).
            options.AddPolicy(AnySessionPolicy, policy => policy
                .AddAuthenticationSchemes(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, AdminScheme)
                .RequireAuthenticatedUser());

            foreach (var permission in Permissions.Platform)
            {
                options.AddPolicy(permission, policy =>
                {
                    if (IsPlatformStaffPermission(permission))
                    {
                        policy.AddAuthenticationSchemes(AdminScheme);
                    }

                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission));
                });
            }
        });

        return services;
    }

    public static bool IsPlatformStaffPermission(string permission) =>
        permission.StartsWith("admin.", StringComparison.Ordinal)
        || permission.StartsWith("support.", StringComparison.Ordinal)
        || permission.StartsWith("finance.", StringComparison.Ordinal);
}
