using Malabak.Api.Infrastructure.Logging;
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

                        // Same redaction as the request log (tokens masked, player coordinates rounded).
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            if (request.QueryString.HasValue)
                            {
                                activity.SetTag("url.query", QueryStringRedactor.RedactQueryString(request.QueryString).TrimStart('?'));
                            }
                        };
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
                    .AddMeter("Malabak.Outbox")
                    .AddMeter(Malabak.Modules.Venues.Application.Availability.CachedVenueAvailability.MeterName);
            });

        return services;
    }
}
