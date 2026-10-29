using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Reads security secrets without hardcoded fallbacks: outside Development a missing secret fails closed;
/// in Development a random per-process secret is used so nothing secret ever lives in source control.
/// </summary>
internal static class SecurityConfiguration
{
    public static bool IsDevelopment(IConfiguration configuration)
    {
        var environment = configuration["environment"]
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"];

        return string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);
    }

    public static byte[] RequireSecret(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return Encoding.UTF8.GetBytes(value);
        }

        if (IsDevelopment(configuration))
        {
            return RandomNumberGenerator.GetBytes(32);
        }

        throw new InvalidOperationException($"Required secret '{key}' is not configured.");
    }
}
