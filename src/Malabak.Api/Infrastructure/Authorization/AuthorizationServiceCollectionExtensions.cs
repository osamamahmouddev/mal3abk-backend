using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Malabak.Api.Infrastructure.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Deny by default (every endpoint needs an authenticated user unless marked AllowAnonymous) plus one
    /// policy per platform permission, named after the permission (e.g. <c>RequireAuthorization(Permissions.VenuesManage)</c>).
    /// </summary>
    public static IServiceCollection AddMalabakAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            foreach (var permission in Permissions.Platform)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission)));
            }
        });

        return services;
    }
}
