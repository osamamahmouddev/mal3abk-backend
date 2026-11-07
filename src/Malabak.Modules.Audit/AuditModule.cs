using Malabak.Modules.Audit.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Audit;

public static class AuditModule
{
    public const string Schema = "audit";

    /// <summary>Registers the audit trail services. The host registers <see cref="AuditDbContext"/> and <see cref="IAuditRequestContext"/>.</summary>
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddScoped<IAuditLog, AuditLogService>();
        services.AddScoped<AuditLogQuery>();
        return services;
    }
}
