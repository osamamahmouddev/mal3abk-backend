using Malabak.Modules.Venues.Application.Common;

namespace Malabak.Api.Infrastructure.Authorization;

/// <summary>
/// Decides, for every request under <c>/venues/{venueId}</c>, whether the caller may act on that venue at all and, when a
/// permission is given, whether their role there includes it. The venue id comes from the URL, so it is never trusted on its
/// own: a venue the caller has no part in looks like one that does not exist (404), and a staff member whose role lacks the
/// permission is refused (403). The platform permission <c>venues.manage</c> only opens the surface; it says nothing about
/// which venue.
/// </summary>
public sealed class VenueAccessEndpointFilter : IEndpointFilter
{
    private readonly string? _permission;

    public VenueAccessEndpointFilter(string? permission = null)
    {
        _permission = permission;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        if (!httpContext.Request.RouteValues.TryGetValue("venueId", out var raw) || !Guid.TryParse(raw?.ToString(), out var venueId))
        {
            return Results.NotFound();
        }

        var access = await httpContext.RequestServices.GetRequiredService<IVenueAccessService>()
            .GetAccessAsync(userId.Value, venueId, httpContext.RequestAborted);

        if (!access.HasAccess)
        {
            return Results.NotFound();
        }

        if (_permission is not null && !access.Can(_permission))
        {
            return Results.Problem(title: "Forbidden", detail: "Your role at this venue does not allow this action.", statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}

public static class VenueAccessEndpointExtensions
{
    /// <summary>Requires the caller to have access to the venue in the route, and to hold this venue-scoped permission there.</summary>
    public static RouteHandlerBuilder RequireVenuePermission(this RouteHandlerBuilder builder, string permission) =>
        builder.AddEndpointFilter(new VenueAccessEndpointFilter(permission));
}
