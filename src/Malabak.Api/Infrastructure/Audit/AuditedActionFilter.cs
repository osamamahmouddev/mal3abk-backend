using Malabak.Api.Infrastructure.Authorization;
using Malabak.SharedKernel.Contracts;

namespace Malabak.Api.Infrastructure.Audit;

/// <summary>
/// Records an admin action in the audit trail when the endpoint succeeds (2xx). For older endpoints whose handlers predate
/// the audit log; new services record richer before/after snapshots themselves. Request bodies are never copied.
/// </summary>
public sealed class AuditedActionFilter(string action, string entityType, string routeKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var status = result is IStatusCodeHttpResult { StatusCode: { } code } ? code : StatusCodes.Status200OK;
        if (status is >= 200 and < 300)
        {
            var http = context.HttpContext;
            var entityId = http.Request.RouteValues.TryGetValue(routeKey, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
            var audit = http.RequestServices.GetRequiredService<IAuditLog>();
            await audit.RecordAsync(new AuditEntry(action, entityType, entityId, http.User.GetUserId(), AuditActorType.Admin), http.RequestAborted);
        }

        return result;
    }
}

public static class AuditedActionExtensions
{
    public static RouteHandlerBuilder Audited(this RouteHandlerBuilder builder, string action, string entityType, string routeKey = "id") =>
        builder.AddEndpointFilter(new AuditedActionFilter(action, entityType, routeKey));
}
