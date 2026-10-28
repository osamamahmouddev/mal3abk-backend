using Malabak.Modules.Notifications.Application;
using Malabak.SharedKernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Notifications;

public static class NotificationsModule
{
    public const string Schema = "notifications";

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<INotificationPublisher, OutboxNotificationPublisher>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<UserNotificationService>();
        return services;
    }
}
