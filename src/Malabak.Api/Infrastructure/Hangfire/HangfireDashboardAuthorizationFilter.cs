using System.Security.Claims;
using Hangfire.Dashboard;
using Malabak.Api.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Hangfire;

/// <summary>
/// Secures the Hangfire Dashboard adhering to SECURITY.md (Admin only, deny-by-default, Cloudflare Access).
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    private const string _cfAccessEmailHeader = "Cf-Access-Authenticated-User-Email";
    private readonly HangfireOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<HangfireDashboardAuthorizationFilter> _logger;

    public HangfireDashboardAuthorizationFilter(
        IOptions<HangfireOptions> options,
        IHostEnvironment environment,
        ILogger<HangfireDashboardAuthorizationFilter> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Authorize(DashboardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = context.GetHttpContext();
        if (httpContext is null)
        {
            return false;
        }

        // 1. Optional bypass only allowed in Development environment
        if (_environment.IsDevelopment() && _options.BypassDashboardAuthInDevelopment)
        {
            _logger.LogWarning("Hangfire Dashboard authorization bypassed in Development mode per configuration.");
            return true;
        }

        // 2. Cloudflare Access verification if configured
        if (!string.IsNullOrWhiteSpace(_options.CloudflareAccessTeamDomain) &&
            (!httpContext.Request.Headers.TryGetValue(_cfAccessEmailHeader, out var cfEmail) ||
             string.IsNullOrWhiteSpace(cfEmail)))
        {
            _logger.LogWarning("Access to Hangfire Dashboard rejected: missing '{Header}' header.", _cfAccessEmailHeader);
            return false;
        }

        // 3. Deny-by-default: User must be authenticated
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning(
                "Access to Hangfire Dashboard rejected: Unauthenticated request from IP {RemoteIp}.",
                httpContext.Connection.RemoteIpAddress);
            return false;
        }

        // 4. Role Authorization: Must possess Admin role or claim
        var isAdmin = httpContext.User.IsInRole("Admin") ||
                      httpContext.User.IsInRole("admin") ||
                      httpContext.User.HasClaim(c =>
                          (c.Type == ClaimTypes.Role || c.Type == "role" || c.Type == "client_role") &&
                          string.Equals(c.Value, "admin", StringComparison.OrdinalIgnoreCase));

        if (!isAdmin)
        {
            _logger.LogWarning(
                "Access to Hangfire Dashboard rejected: User '{User}' lacks Admin role.",
                httpContext.User.Identity?.Name ?? "anonymous");
            return false;
        }

        return true;
    }
}
