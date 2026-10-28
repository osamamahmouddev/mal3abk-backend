using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Configuration;

public static class ConfigurationExtensions
{
    public static ConfigurationManager ConfigureMalabakConfigurationSources(
        this ConfigurationManager configuration,
        IWebHostEnvironment environment)
    {
        // 1. User Secrets in Development
        if (environment.IsDevelopment())
        {
            configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
        }

        // 2. Environment variables with MALABAK_ prefix for staging/production cloud secret injection
        configuration.AddEnvironmentVariables(prefix: "MALABAK_");

        return configuration;
    }

    public static IServiceCollection AddMalabakOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // Database Options with validation
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options =>
            {
                if (!environment.IsDevelopment() && options.EnableSensitiveDataLogging)
                {
                    return false; // Sensitive data logging is forbidden outside Development
                }

                return true;
            }, "EnableSensitiveDataLogging must not be enabled outside of Development.")
            .ValidateOnStart();

        // Redis Options with validation
        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // JWT Options with validation
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "JWT Issuer must be configured.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "JWT Audience must be configured.")
            .Validate(options => options.AccessTokenExpirationMinutes > 0 && options.AccessTokenExpirationMinutes <= 60, "Access token expiration must be between 1 and 60 minutes.")
            .ValidateOnStart();

        // Cloudflare Turnstile Options
        services.AddOptions<TurnstileOptions>()
            .Bind(configuration.GetSection(TurnstileOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // CORS Options
        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Rate Limiter Options
        services.AddOptions<RateLimiterOptions>()
            .Bind(configuration.GetSection(RateLimiterOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
