using System.Text.Json;
using Malabak.SharedKernel;

namespace Malabak.Modules.Notifications.Domain;

public enum NotificationCategory
{
    Bookings = 0,
    Payments = 1,
    Venues = 2,
    Teams = 3,
    Security = 4,
    Marketing = 5
}

/// <summary>
/// The app a notification is shown in. One account can hold several hats (a player who also works at a venue), so each app
/// asks for its own surface instead of listing everything the account ever received.
/// </summary>
public enum NotificationSurface
{
    /// <summary>The player app: bookings, teams, matches.</summary>
    Player = 0,

    /// <summary>The venue dashboard: payment proofs, venue status, staff, subscription.</summary>
    Venue = 1,

    /// <summary>The admin console. Admins have no user notifications yet, so this only returns the ones meant for every app.</summary>
    Admin = 2
}

/// <summary>An in-app notification. Text is rendered at read time from the template in the reader's language.</summary>
public sealed class Notification : Entity<Guid>
{
    private Notification()
    {
    }

    public Guid UserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public NotificationCategory Category { get; private set; }
    public string DataJson { get; private set; } = "{}";
    public string? DeepLink { get; private set; }
    public string DedupeKey { get; private set; } = string.Empty;
    public DateTime? ReadAtUtc { get; private set; }

    public static Notification Create(
        Guid userId, string type, NotificationCategory category, IReadOnlyDictionary<string, string> data, string? deepLink, string dedupeKey, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupeKey);
        return new Notification
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Type = type,
            Category = category,
            DataJson = JsonSerializer.Serialize(data),
            DeepLink = deepLink,
            DedupeKey = dedupeKey,
            CreatedAtUtc = nowUtc
        };
    }

    public IReadOnlyDictionary<string, string> Data =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(DataJson) ?? new Dictionary<string, string>();

    public void MarkRead(DateTime nowUtc)
    {
        ReadAtUtc ??= nowUtc;
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>Per-user, per-category opt-outs. Missing row = defaults (everything on except marketing).</summary>
public sealed class NotificationPreference
{
    private NotificationPreference()
    {
    }

    public Guid UserId { get; private set; }
    public NotificationCategory Category { get; private set; }
    public bool InAppEnabled { get; private set; }
    public bool PushEnabled { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static NotificationPreference Create(Guid userId, NotificationCategory category, bool inApp, bool push, DateTime nowUtc)
    {
        var preference = new NotificationPreference { UserId = userId, Category = category };
        preference.Update(inApp, push, nowUtc);
        return preference;
    }

    /// <summary>Security alerts can't be switched off: they're how a user learns their account is at risk.</summary>
    public void Update(bool inApp, bool push, DateTime nowUtc)
    {
        InAppEnabled = Category == NotificationCategory.Security || inApp;
        PushEnabled = Category == NotificationCategory.Security || push;
        UpdatedAtUtc = nowUtc;
    }

    public static (bool InApp, bool Push) Defaults(NotificationCategory category) =>
        category == NotificationCategory.Marketing ? (false, false) : (true, true);
}

/// <summary>Outbox row written by publishers; the dispatcher processes it at-least-once (dedupe key makes it idempotent).</summary>
public sealed class NotificationOutboxMessage
{
    public const int MaxAttempts = 5;

    private NotificationOutboxMessage()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string DataJson { get; private set; } = "{}";
    public string DedupeKey { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }

    public static NotificationOutboxMessage Create(Guid userId, string type, IReadOnlyDictionary<string, string> data, string dedupeKey, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupeKey);
        return new NotificationOutboxMessage
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Type = type,
            DataJson = JsonSerializer.Serialize(data),
            DedupeKey = dedupeKey.Length <= 200 ? dedupeKey : dedupeKey[..200],
            CreatedAtUtc = nowUtc
        };
    }

    public IReadOnlyDictionary<string, string> Data =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(DataJson) ?? new Dictionary<string, string>();

    public void MarkProcessed(DateTime nowUtc)
    {
        ProcessedAtUtc = nowUtc;
        Error = null;
    }

    /// <summary>Records a failure; after <see cref="MaxAttempts"/> the message is parked (processed with an error) for inspection.</summary>
    public void MarkFailed(string error, DateTime nowUtc)
    {
        Attempts++;
        Error = error.Length <= 500 ? error : error[..500];
        if (Attempts >= MaxAttempts)
        {
            ProcessedAtUtc = nowUtc;
        }
    }
}
