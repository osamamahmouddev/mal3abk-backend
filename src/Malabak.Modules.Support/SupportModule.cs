using Malabak.Modules.Support.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Support;

public static class SupportModule
{
    public const string Schema = "support";

    /// <summary>Support tickets (TASK-706). The host registers <see cref="SupportDbContext"/>.</summary>
    public static IServiceCollection AddSupportModule(this IServiceCollection services)
    {
        services.AddScoped<SupportTicketService>();
        services.AddScoped<IVenueComplaintsReader>(sp => sp.GetRequiredService<SupportTicketService>());
        services.AddScoped<IAdminMetricsProvider, SupportMetricsProvider>();
        return services;
    }
}
