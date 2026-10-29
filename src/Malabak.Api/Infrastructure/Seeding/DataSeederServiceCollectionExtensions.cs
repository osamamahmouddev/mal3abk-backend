using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Api.Infrastructure.Seeding;

public static class DataSeederServiceCollectionExtensions
{
    public static IServiceCollection AddMalabakDataSeeders(this IServiceCollection services)
    {
        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<DemoDataSeeder>();
        return services;
    }
}
