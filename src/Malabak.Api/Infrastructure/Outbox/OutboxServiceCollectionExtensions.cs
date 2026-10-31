using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel.Events;
using Malabak.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Api.Infrastructure.Outbox;

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddMalabakOutbox(this IServiceCollection services)
    {
        // 1. Dispatcher core
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();

        // 2. Register outbox message sources for known module DbContexts
        services.AddScoped<IOutboxMessageSource, OutboxMessageSource<IdentityDbContext>>();
        services.AddScoped<IOutboxMessageSource, OutboxMessageSource<VenuesDbContext>>();
        services.AddScoped<IOutboxMessageSource, OutboxMessageSource<BookingDbContext>>();

        // 3. Register Inbox store default
        services.AddScoped<IInboxStore, EfInboxStore<IdentityDbContext>>();

        // 4. Background service and Hangfire job
        services.AddHostedService<OutboxDispatcherBackgroundService>();
        services.AddTransient<OutboxDispatcherJob>();

        return services;
    }

    public static IServiceCollection AddOutboxSource<TDbContext>(this IServiceCollection services)
        where TDbContext : BaseDbContext
    {
        services.AddScoped<IOutboxMessageSource, OutboxMessageSource<TDbContext>>();
        return services;
    }
}
