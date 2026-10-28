using Malabak.Modules.Notifications.Application;
using Malabak.Modules.Notifications.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Notifications;

public static class NotificationsModule
{
    public const string Schema = "notifications";

    /// <param name="fcm">Firebase settings; null disables push (in-app notifications still work).</param>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, FcmOptions? fcm = null)
    {
        services.AddScoped<INotificationPublisher, OutboxNotificationPublisher>();
        services.AddScoped<DeviceTokenService>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<PushDispatcher>();
        services.AddScoped<UserNotificationService>();

        if (fcm is not null)
        {
            services.AddSingleton<IPushSender>(sp => new FcmPushSender(fcm, sp.GetRequiredService<IClock>()));
        }
        else
        {
            services.AddSingleton<IPushSender, DisabledPushSender>();
        }

        return services;
    }
}
