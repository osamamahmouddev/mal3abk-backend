using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Api.Infrastructure.Validation;

public static class ValidationExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter<ValidationEndpointFilter<TRequest>>();
    }

    public static IServiceCollection AddMalabakValidation(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(Program).Assembly, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(Malabak.Modules.Identity.IdentityModule).Assembly, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(Malabak.Modules.Booking.Infrastructure.BookingDbContext).Assembly, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(Malabak.Modules.Venues.VenuesModule).Assembly, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(Malabak.Modules.Payments.PaymentsModule).Assembly, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(Malabak.Modules.Matches.MatchesModule).Assembly, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(Malabak.Modules.Teams.TeamsModule).Assembly, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(Malabak.Modules.Notifications.NotificationsModule).Assembly, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(Malabak.Modules.Subscriptions.SubscriptionsModule).Assembly, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(Malabak.Modules.Support.SupportModule).Assembly, includeInternalTypes: true);
        services.AddValidatorsFromAssembly(typeof(Malabak.Modules.Audit.AuditModule).Assembly, includeInternalTypes: true);

        return services;
    }
}
