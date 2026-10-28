using System.Net;

namespace Malabak.Api.Infrastructure.Authorization;

/// <summary>
/// Optional IP allowlist for admin endpoints (Security:Admin:AllowedIpRanges, CIDR list). Empty = no restriction;
/// in production this sits behind Cloudflare Access as well (SECURITY.md §3.9).
/// </summary>
public sealed class AdminIpAllowlistFilter : IEndpointFilter
{
    private readonly IPNetwork[] _allowed;

    public AdminIpAllowlistFilter(IConfiguration configuration)
    {
        _allowed = (configuration.GetSection("Security:Admin:AllowedIpRanges").Get<string[]>() ?? [])
            .Select(range => IPNetwork.Parse(range.Trim()))
            .ToArray();
    }

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (_allowed.Length == 0)
        {
            return next(context);
        }

        var remote = context.HttpContext.Connection.RemoteIpAddress;
        if (remote is not null && remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        return remote is not null && Array.Exists(_allowed, network => network.Contains(remote))
            ? next(context)
            : ValueTask.FromResult<object?>(Results.Problem(title: "Admin.IpNotAllowed", statusCode: StatusCodes.Status403Forbidden));
    }
}
