using Microsoft.Extensions.Hosting;

namespace Malabak.Modules.Identity;

/// <summary>
/// Development and Testing are the only environments where security shortcuts (Turnstile bypass and
/// test tokens, built-in HMAC keys, a throwaway JWT signing key) are allowed. Every other environment,
/// Staging included, must be configured with real secrets or fail at startup (SECURITY.md).
/// </summary>
public static class IdentityEnvironment
{
    public const string Testing = "Testing";

    public static bool AllowsDevelopmentShortcuts(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsDevelopment() || environment.IsEnvironment(Testing);
    }
}
