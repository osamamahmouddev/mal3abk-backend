using Malabak.Modules.Subscriptions.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Subscriptions;

public static class SubscriptionsModule
{
    public const string Schema = "subscriptions";

    /// <summary>Plans, subscriptions, entitlements and invoices. The host registers <see cref="SubscriptionsDbContext"/>.</summary>
    public static IServiceCollection AddSubscriptionsModule(this IServiceCollection services)
    {
        services.AddScoped<EntitlementService>();
        services.AddScoped<IEntitlementService>(sp => sp.GetRequiredService<EntitlementService>());
        services.AddScoped<PlanService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<IInvoiceNumberGenerator, InvoiceNumberGenerator>();
        services.AddScoped<SubscriptionLifecycleJob>();
        return services;
    }
}
