using Malabak.Modules.Venues.Application.Common;

namespace Malabak.Api.Infrastructure.Authorization;

/// <summary>
/// Restricts the Venues module's query filters to the caller's venues for the rest of the request,
/// so a handler that forgets an ownership check still can't read another venue's rows.
/// </summary>
public sealed class VenueTenantEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var services = httpContext.RequestServices;
        var accessible = await services.GetRequiredService<IVenueAccessService>()
            .GetAccessibleVenuesAsync(userId.Value, httpContext.RequestAborted);

        services.GetRequiredService<IVenueTenantContext>().RestrictTo(accessible.Select(a => a.VenueId));

        return await next(context);
    }
}
