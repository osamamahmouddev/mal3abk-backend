using System.Globalization;
using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Modules.Notifications.Application;
using Malabak.Modules.Notifications.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/me")
            .WithApiVersionSet(apiVersionSet)
            .MapToApiVersion(1, 0)
            .RequireAuthorization()
            .WithTags("Notifications");

        group.MapGet("/notifications", async (ClaimsPrincipal user, [FromQuery] int? page, [FromQuery] int? pageSize,
            UserNotificationService notifications, CancellationToken ct) =>
            Results.Ok(await notifications.ListAsync(user.GetUserId()!.Value, page ?? 1, pageSize ?? 20, IsArabic(), ct)))
        .WithName("GetMyNotifications").WithSummary("Paged notifications in the request language (Accept-Language ar|en)");

        group.MapGet("/notifications/unread-count", async (ClaimsPrincipal user, UserNotificationService notifications, CancellationToken ct) =>
            Results.Ok(new { UnreadCount = await notifications.UnreadCountAsync(user.GetUserId()!.Value, ct) }))
        .WithName("GetUnreadNotificationCount");

        group.MapPost("/notifications/{id:guid}/read", async (Guid id, ClaimsPrincipal user, UserNotificationService notifications, CancellationToken ct) =>
            await notifications.MarkReadAsync(user.GetUserId()!.Value, id, ct)
                ? Results.NoContent()
                : Results.Problem(title: "Notification.NotFound", statusCode: StatusCodes.Status404NotFound))
        .WithName("MarkNotificationRead");

        group.MapPost("/notifications/read-all", async (ClaimsPrincipal user, UserNotificationService notifications, CancellationToken ct) =>
            Results.Ok(new { Marked = await notifications.MarkAllReadAsync(user.GetUserId()!.Value, ct) }))
        .WithName("MarkAllNotificationsRead");

        group.MapGet("/notification-preferences", async (ClaimsPrincipal user, UserNotificationService notifications, CancellationToken ct) =>
            Results.Ok(await notifications.GetPreferencesAsync(user.GetUserId()!.Value, ct)))
        .WithName("GetNotificationPreferences");

        group.MapPut("/notification-preferences/{category}", async (NotificationCategory category, ClaimsPrincipal user,
            [FromBody] PreferenceApiRequest request, UserNotificationService notifications, CancellationToken ct) =>
            Results.Ok(await notifications.UpdatePreferenceAsync(user.GetUserId()!.Value, category, request.InAppEnabled, request.PushEnabled, ct)))
        .WithName("UpdateNotificationPreference").WithSummary("Security alerts stay on regardless");
    }

    // Arabic is the primary language; RequestLocalization has already set the culture from Accept-Language.
    private static bool IsArabic() => !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);

    public sealed record PreferenceApiRequest(bool InAppEnabled, bool PushEnabled);
}
