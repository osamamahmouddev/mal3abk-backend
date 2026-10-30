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
        .Produces<NotificationPageDto>().WithName("GetMyNotifications").WithSummary("Paged notifications in the request language (Accept-Language ar|en)");

        group.MapGet("/notifications/unread-count", async (ClaimsPrincipal user, UserNotificationService notifications, CancellationToken ct) =>
            Results.Ok(new UnreadCountResponse(await notifications.UnreadCountAsync(user.GetUserId()!.Value, ct))))
        .Produces<UnreadCountResponse>().WithName("GetUnreadNotificationCount");

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
        .Produces<IReadOnlyList<NotificationPreferenceDto>>().WithName("GetNotificationPreferences");

        group.MapPut("/notification-preferences/{category}", async (NotificationCategory category, ClaimsPrincipal user,
            [FromBody] PreferenceApiRequest request, UserNotificationService notifications, CancellationToken ct) =>
            Results.Ok(await notifications.UpdatePreferenceAsync(user.GetUserId()!.Value, category, request.InAppEnabled, request.PushEnabled, ct)))
        .Produces<IReadOnlyList<NotificationPreferenceDto>>().WithName("UpdateNotificationPreference").WithSummary("Security alerts stay on regardless");

        // --- Push device tokens (bound to the current device session) ---

        group.MapPost("/devices/push-token", async (ClaimsPrincipal user, [FromBody] PushTokenApiRequest request,
            DeviceTokenService devices, CancellationToken ct) =>
        {
            var sessionId = user.GetSessionId();
            if (sessionId is null || string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > DeviceToken.MaxTokenLength)
            {
                return Results.Problem(title: "Push.InvalidToken", statusCode: StatusCodes.Status400BadRequest);
            }

            await devices.RegisterAsync(user.GetUserId()!.Value, sessionId.Value, request.Token, request.Platform, IsArabic() ? "ar" : "en", ct);
            return Results.NoContent();
        })
        .WithName("RegisterPushToken").WithSummary("Register this device's FCM token; push language follows Accept-Language");

        group.MapDelete("/devices/push-token", async (ClaimsPrincipal user, [FromBody] UnregisterPushTokenApiRequest request,
            DeviceTokenService devices, CancellationToken ct) =>
        {
            await devices.UnregisterAsync(user.GetUserId()!.Value, request.Token ?? string.Empty, ct);
            return Results.NoContent(); // idempotent; doesn't reveal whether the token existed
        })
        .WithName("UnregisterPushToken");
    }

    // Arabic is the primary language; RequestLocalization has already set the culture from Accept-Language.
    private static bool IsArabic() => !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);

    public sealed record PreferenceApiRequest(bool InAppEnabled, bool PushEnabled);

    public sealed record PushTokenApiRequest(string Token, DevicePlatform Platform);

    public sealed record UnregisterPushTokenApiRequest(string? Token);

    public sealed record UnreadCountResponse(int UnreadCount);
}
