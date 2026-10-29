using Malabak.Modules.Notifications.Domain;
using Malabak.Modules.Notifications.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Notifications.Application;

/// <summary>What actually leaves our servers: title, a generic line and ids — never booking details, reasons or codes.</summary>
public sealed record PushPayload(string Token, string Title, string Body, IReadOnlyDictionary<string, string> Data);

public enum PushFailureKind
{
    None = 0,
    Transient = 1,
    InvalidToken = 2,
    Permanent = 3
}

public sealed record PushSendResult(PushFailureKind Failure, string? Error)
{
    public static readonly PushSendResult Ok = new(PushFailureKind.None, null);
    public bool Success => Failure == PushFailureKind.None;
}

/// <summary>Push vendor (FCM). Implementations must not log payload text.</summary>
public interface IPushSender
{
    Task<PushSendResult> SendAsync(PushPayload payload, CancellationToken cancellationToken = default);
}

/// <summary>Registers device tokens for the signed-in session and queues pushes for new notifications.</summary>
public sealed class DeviceTokenService
{
    private readonly NotificationsDbContext _dbContext;
    private readonly IClock _clock;

    public DeviceTokenService(NotificationsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task RegisterAsync(Guid userId, Guid sessionId, string token, DevicePlatform platform, string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > DeviceToken.MaxTokenLength)
        {
            throw new ArgumentException("Invalid device token.", nameof(token));
        }

        var nowUtc = _clock.UtcNow;
        var existing = await _dbContext.DeviceTokens.FirstOrDefaultAsync(t => t.Token == token.Trim(), cancellationToken);
        if (existing is null)
        {
            _dbContext.DeviceTokens.Add(DeviceToken.Create(userId, sessionId, token, platform, language, nowUtc));
        }
        else
        {
            existing.Assign(userId, sessionId, platform, language, nowUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Only the owner can unregister their token; returns false otherwise (no cross-user probing).</summary>
    public async Task<bool> UnregisterAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var device = await _dbContext.DeviceTokens.FirstOrDefaultAsync(t => t.Token == token.Trim() && t.UserId == userId, cancellationToken);
        if (device is null)
        {
            return false;
        }

        device.Deactivate("unregistered");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Queues one delivery per active device; the unique (notification, device) index makes this idempotent.</summary>
    public async Task QueueAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        var devices = await _dbContext.DeviceTokens.AsNoTracking()
            .Where(t => t.UserId == notification.UserId && t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (var deviceId in devices)
        {
            _dbContext.PushDeliveries.Add(PushDelivery.Create(notification.Id, deviceId, _clock.UtcNow));
        }
    }
}

/// <summary>Sends due pushes with retry/backoff, deactivating tokens FCM rejects and tokens of signed-out sessions.</summary>
public sealed class PushDispatcher
{
    private readonly NotificationsDbContext _dbContext;
    private readonly IPushSender _sender;
    private readonly ISessionActivityChecker _sessions;
    private readonly IClock _clock;
    private readonly ILogger<PushDispatcher> _logger;

    public PushDispatcher(NotificationsDbContext dbContext, IPushSender sender, ISessionActivityChecker sessions, IClock clock, ILogger<PushDispatcher> logger)
    {
        _dbContext = dbContext;
        _sender = sender;
        _sessions = sessions;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> ProcessDueAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var due = await _dbContext.PushDeliveries
            .Where(d => d.Status == PushStatus.Pending && d.NextAttemptAtUtc <= nowUtc)
            .OrderBy(d => d.NextAttemptAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var delivery in due)
        {
            await ProcessAsync(delivery, nowUtc, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    private async Task ProcessAsync(PushDelivery delivery, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var device = await _dbContext.DeviceTokens.FirstOrDefaultAsync(t => t.Id == delivery.DeviceTokenId, cancellationToken);
        var notification = await _dbContext.Notifications.AsNoTracking().FirstOrDefaultAsync(n => n.Id == delivery.NotificationId, cancellationToken);
        if (device is null || notification is null || !device.IsActive)
        {
            delivery.MarkPermanentFailure("device_or_notification_gone");
            return;
        }

        if (!await _sessions.IsActiveAsync(device.SessionId, cancellationToken))
        {
            device.Deactivate("session_ended");
            delivery.MarkPermanentFailure("session_ended");
            return;
        }

        var result = await _sender.SendAsync(BuildPayload(device, notification), cancellationToken);
        switch (result.Failure)
        {
            case PushFailureKind.None:
                delivery.MarkSent();
                break;
            case PushFailureKind.InvalidToken:
                device.Deactivate("invalid_token");
                delivery.MarkPermanentFailure("invalid_token");
                break;
            case PushFailureKind.Transient:
                delivery.ScheduleRetry(result.Error ?? "transient", nowUtc);
                break;
            default:
                delivery.MarkPermanentFailure(result.Error ?? "permanent");
                _logger.LogWarning("Push {DeliveryId} failed permanently: {Error}", delivery.Id, result.Error);
                break;
        }
    }

    internal static PushPayload BuildPayload(DeviceToken device, Notification notification)
    {
        var arabic = device.Language != "en";
        var title = "Malabak";
        if (NotificationCatalog.TryGet(notification.Type, out var template))
        {
            title = NotificationCatalog.Render(arabic ? template.TitleAr : template.TitleEn, notification.Data);
        }

        // Body is deliberately generic: lock screens are visible to anyone holding the phone.
        var body = arabic ? "افتح ملعبك لعرض التفاصيل" : "Open Malabak for details";
        var data = new Dictionary<string, string>
        {
            ["notificationId"] = notification.Id.ToString("D"),
            ["type"] = notification.Type
        };

        if (notification.DeepLink is not null)
        {
            data["deepLink"] = notification.DeepLink;
        }

        return new PushPayload(device.Token, title, body, data);
    }
}
