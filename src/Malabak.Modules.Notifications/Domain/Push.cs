namespace Malabak.Modules.Notifications.Domain;

public enum DevicePlatform
{
    Android = 0,
    Ios = 1,
    Web = 2
}

/// <summary>An FCM registration token for one signed-in device session.</summary>
public sealed class DeviceToken
{
    public const int MaxTokenLength = 4096;

    private DeviceToken()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid SessionId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public DevicePlatform Platform { get; private set; }
    public string Language { get; private set; } = "ar";
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }
    public string? DeactivatedReason { get; private set; }

    public static DeviceToken Create(Guid userId, Guid sessionId, string token, DevicePlatform platform, string language, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var device = new DeviceToken { Id = Guid.CreateVersion7(), Token = token.Trim(), CreatedAtUtc = nowUtc };
        device.Assign(userId, sessionId, platform, language, nowUtc);
        return device;
    }

    /// <summary>Re-registering a token (app reinstall, a different user signing in on the same phone) moves it to the new owner.</summary>
    public void Assign(Guid userId, Guid sessionId, DevicePlatform platform, string language, DateTime nowUtc)
    {
        UserId = userId;
        SessionId = sessionId;
        Platform = platform;
        Language = language == "en" ? "en" : "ar";
        IsActive = true;
        DeactivatedReason = null;
        LastSeenAtUtc = nowUtc;
    }

    public void Deactivate(string reason)
    {
        IsActive = false;
        DeactivatedReason = reason;
    }
}

public enum PushStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

/// <summary>One push to one device for one notification, retried with exponential backoff.</summary>
public sealed class PushDelivery
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);

    private PushDelivery()
    {
    }

    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public Guid DeviceTokenId { get; private set; }
    public PushStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string? Error { get; private set; }

    public static PushDelivery Create(Guid notificationId, Guid deviceTokenId, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        NotificationId = notificationId,
        DeviceTokenId = deviceTokenId,
        Status = PushStatus.Pending,
        NextAttemptAtUtc = nowUtc,
        CreatedAtUtc = nowUtc
    };

    public void MarkSent()
    {
        Status = PushStatus.Sent;
        Attempts++;
        Error = null;
    }

    public void MarkPermanentFailure(string error)
    {
        Status = PushStatus.Failed;
        Attempts++;
        Error = Clip(error);
    }

    /// <summary>Transient failure: wait 30s, 60s, 120s, … and give up after <see cref="MaxAttempts"/>.</summary>
    public void ScheduleRetry(string error, DateTime nowUtc)
    {
        Attempts++;
        Error = Clip(error);
        if (Attempts >= MaxAttempts)
        {
            Status = PushStatus.Failed;
            return;
        }

        NextAttemptAtUtc = nowUtc + TimeSpan.FromTicks(BaseBackoff.Ticks * (1L << (Attempts - 1)));
    }

    private static string Clip(string value) => value.Length <= 300 ? value : value[..300];
}
