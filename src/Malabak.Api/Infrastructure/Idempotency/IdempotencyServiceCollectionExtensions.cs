using Malabak.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Api.Infrastructure.Idempotency;

public static class IdempotencyServiceCollectionExtensions
{
    public static IServiceCollection AddMalabakIdempotency(this IServiceCollection services)
    {
        services.AddSingleton<IRequestHasher, Sha256RequestHasher>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore<IdentityDbContext>>();
        services.AddScoped<IdempotentEndpointFilter>();
        services.AddTransient<IdempotencyCleanupJob>();

        return services;
    }

    public static IApplicationBuilder UseMalabakIdempotency(this IApplicationBuilder app)
    {
        return app.UseMiddleware<IdempotencyBufferingMiddleware>();
    }

    public static RouteHandlerBuilder RequireIdempotency(this RouteHandlerBuilder builder, bool required = true)
    {
        return builder
            .WithMetadata(new IdempotentAttribute(required))
            .AddEndpointFilter<IdempotentEndpointFilter>();
    }

    public static RouteGroupBuilder RequireIdempotency(this RouteGroupBuilder builder, bool required = true)
    {
        return builder
            .WithMetadata(new IdempotentAttribute(required))
            .AddEndpointFilter<IdempotentEndpointFilter>();
    }
}
