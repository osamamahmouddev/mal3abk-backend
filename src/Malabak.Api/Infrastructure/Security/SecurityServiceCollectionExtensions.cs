using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddMalabakSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CloudflareOptions>()
            .Bind(configuration.GetSection(CloudflareOptions.SectionName));

        services.AddOptions<MalabakCorsOptions>()
            .Bind(configuration.GetSection(MalabakCorsOptions.SectionName));

        services.AddSingleton<IDistributedRateLimiter, RedisDistributedRateLimiter>();

        var corsSection = configuration.GetSection(MalabakCorsOptions.SectionName);
        var corsOptions = corsSection.Get<MalabakCorsOptions>() ?? new MalabakCorsOptions();

        services.AddCors(options =>
        {
            options.AddPolicy(MalabakCorsOptions.PolicyName, policy =>
            {
                policy.WithOrigins([.. corsOptions.AllowedOrigins])
                    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                    .WithHeaders("Authorization", "Content-Type", "X-CSRF-Token", "Idempotency-Key", "Accept-Language", "X-Locale")
                    .WithExposedHeaders("Idempotent-Replayed", "Retry-After", "X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset")
                    .AllowCredentials()
                    .SetPreflightMaxAge(TimeSpan.FromHours(corsOptions.PreflightMaxAgeHours));
            });
        });

        return services;
    }

    public static IApplicationBuilder UseMalabakSecurity(this IApplicationBuilder app)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<CloudflareRealIpMiddleware>();
        app.UseCors(MalabakCorsOptions.PolicyName);

        return app;
    }

    public static RouteHandlerBuilder RequireDistributedRateLimiting(
        this RouteHandlerBuilder builder,
        string policy,
        int permitLimit = 60,
        int windowSeconds = 60,
        RateLimitAlgorithm algorithm = RateLimitAlgorithm.SlidingWindow,
        bool isSensitive = false)
    {
        var attribute = new DistributedRateLimitAttribute(policy)
        {
            PermitLimit = permitLimit,
            WindowSeconds = windowSeconds,
            Algorithm = algorithm,
            IsSensitive = isSensitive
        };

        return builder
            .WithMetadata(attribute)
            .AddEndpointFilter(new DistributedRateLimitEndpointFilterFactory(attribute));
    }

    private sealed class DistributedRateLimitEndpointFilterFactory : IEndpointFilter
    {
        private readonly DistributedRateLimitAttribute _attribute;

        public DistributedRateLimitEndpointFilterFactory(DistributedRateLimitAttribute attribute)
        {
            _attribute = attribute;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var rateLimiter = context.HttpContext.RequestServices.GetRequiredService<IDistributedRateLimiter>();
            var filter = new DistributedRateLimitEndpointFilter(rateLimiter, _attribute);
            return await filter.InvokeAsync(context, next);
        }
    }
}
