using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Malabak.Api.Infrastructure.OpenTelemetry;

public static class OpenTelemetryExtensions
{
    private const string _serviceName = "Malabak.Api";

    public static IServiceCollection AddMalabakOpenTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(_serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = httpContext =>
                            !httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
                    })
                    .AddHttpClientInstrumentation();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Malabak.Hangfire")
                    .AddMeter("Malabak.Outbox");
            });

        return services;
    }
}
