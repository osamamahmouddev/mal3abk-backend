using Malabak.Modules.Notifications.Domain;
using Malabak.Modules.Notifications.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Notifications.Application;

public sealed record NotificationDto(Guid Id, string Type, string Category, string Title, string Body, string? DeepLink, bool IsRead, DateTime CreatedAtUtc);

public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, int Page, int PageSize, int TotalCount, int UnreadCount);

public sealed record NotificationPreferenceDto(string Category, bool InAppEnabled, bool PushEnabled, bool CanDisable);

/// <summary>Writes publish requests to the module outbox; unknown types are rejected at the source.</summary>
public sealed class OutboxNotificationPublisher : INotificationPublisher
{
    private readonly NotificationsDbContext _dbContext;
    private readonly IClock _clock;

    public OutboxNotificationPublisher(NotificationsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!NotificationCatalog.TryGet(request.Type, out _))
        {
            throw new ArgumentException($"Unknown notification type '{request.Type}'.", nameof(request));
        }

        _dbContext.Outbox.Add(NotificationOutboxMessage.Create(request.UserId, request.Type, request.Data, request.DedupeKey, _clock.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Turns outbox messages into in-app notifications: applies the user's preferences, builds the deep link and
/// skips duplicates by dedupe key, so re-processing after a crash is harmless. Run periodically by the host.
/// </summary>
public sealed class NotificationDispatcher
{
    private readonly NotificationsDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly DeviceTokenService? _push;

    public NotificationDispatcher(NotificationsDbContext dbContext, IClock clock, ILogger<NotificationDispatcher> logger, DeviceTokenService? push = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
        _push = push;
    }

    public async Task<int> ProcessPendingAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var batch = await _dbContext.Outbox
            .Where(m => m.ProcessedAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in batch)
        {
            try
            {
                await DeliverAsync(message, cancellationToken);
                message.MarkProcessed(_clock.UtcNow);
            }
            catch (Exception ex) when (ex is DbUpdateException or ArgumentException or System.Text.Json.JsonException)
            {
                message.MarkFailed(ex.GetType().Name, _clock.UtcNow);
                _logger.LogWarning(ex, "Notification outbox message {MessageId} failed (attempt {Attempt})", message.Id, message.Attempts);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return batch.Count;
    }

    private async Task DeliverAsync(NotificationOutboxMessage message, CancellationToken cancellationToken)
    {
        if (!NotificationCatalog.TryGet(message.Type, out var template))
        {
            throw new ArgumentException($"Unknown notification type '{message.Type}'.");
        }

        var preference = await _dbContext.Preferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == message.UserId && p.Category == template.Category, cancellationToken);
        var inApp = preference?.InAppEnabled ?? NotificationPreference.Defaults(template.Category).InApp;
        if (!inApp)
        {
            return; // opted out: processed, nothing to show
        }

        // Check unsaved notifications from this batch too, not only rows already in the database.
        var duplicate = _dbContext.Notifications.Local.Any(n => n.UserId == message.UserId && n.DedupeKey == message.DedupeKey)
            || await _dbContext.Notifications.AnyAsync(n => n.UserId == message.UserId && n.DedupeKey == message.DedupeKey, cancellationToken);
        if (duplicate)
        {
            return;
        }

        var data = message.Data;
        var notification = Notification.Create(
            message.UserId, message.Type, template.Category, data, template.DeepLink(data), message.DedupeKey, _clock.UtcNow);
        _dbContext.Notifications.Add(notification);

        var push = preference?.PushEnabled ?? NotificationPreference.Defaults(template.Category).Push;
        if (push && _push is not null)
        {
            await _push.QueueAsync(notification, cancellationToken);
        }
    }
}

/// <summary>Read side and preferences for the signed-in user (every query is scoped to that user).</summary>
public sealed class UserNotificationService
{
    public const int MaxPageSize = 50;

    private readonly NotificationsDbContext _dbContext;
    private readonly IClock _clock;

    public UserNotificationService(NotificationsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<NotificationPageDto> ListAsync(Guid userId, int page, int pageSize, bool arabic, NotificationSurface? surface = null, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = ForSurface(_dbContext.Notifications.AsNoTracking().Where(n => n.UserId == userId), surface);
        var total = await query.CountAsync(cancellationToken);
        var unread = await query.CountAsync(n => n.ReadAtUtc == null, cancellationToken);
        var items = await query.OrderByDescending(n => n.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return new NotificationPageDto(items.Select(n => ToDto(n, arabic)).ToList(), page, pageSize, total, unread);
    }

    public Task<int> UnreadCountAsync(Guid userId, NotificationSurface? surface = null, CancellationToken cancellationToken = default) =>
        ForSurface(_dbContext.Notifications.Where(n => n.UserId == userId && n.ReadAtUtc == null), surface).CountAsync(cancellationToken);

    /// <summary>All of the user's notifications when no surface is given (older clients); otherwise only the types shown in that app.</summary>
    private static IQueryable<Notification> ForSurface(IQueryable<Notification> query, NotificationSurface? surface)
    {
        if (surface is not { } wanted)
        {
            return query;
        }

        var types = NotificationCatalog.TypesFor(wanted).ToArray();
        var known = NotificationCatalog.Types.ToArray();
        return wanted == NotificationSurface.Player
            // Player is the default home of any type the catalog does not know (old rows, retired types).
            ? query.Where(n => types.Contains(n.Type) || !known.Contains(n.Type))
            : query.Where(n => types.Contains(n.Type));
    }

    /// <summary>False when the notification doesn't exist or belongs to someone else (indistinguishable to the caller).</summary>
    public async Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        notification.MarkRead(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> MarkAllReadAsync(Guid userId, NotificationSurface? surface = null, CancellationToken cancellationToken = default)
    {
        var unread = await ForSurface(_dbContext.Notifications.Where(n => n.UserId == userId && n.ReadAtUtc == null), surface).ToListAsync(cancellationToken);
        unread.ForEach(n => n.MarkRead(_clock.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return unread.Count;
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var stored = await _dbContext.Preferences.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(cancellationToken);
        return Enum.GetValues<NotificationCategory>().Select(category =>
        {
            var preference = stored.Find(p => p.Category == category);
            var (inApp, push) = preference is null ? NotificationPreference.Defaults(category) : (preference.InAppEnabled, preference.PushEnabled);
            return new NotificationPreferenceDto(category.ToString(), inApp, push, category != NotificationCategory.Security);
        }).ToList();
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> UpdatePreferenceAsync(
        Guid userId, NotificationCategory category, bool inApp, bool push, CancellationToken cancellationToken = default)
    {
        var preference = await _dbContext.Preferences.FirstOrDefaultAsync(p => p.UserId == userId && p.Category == category, cancellationToken);
        if (preference is null)
        {
            _dbContext.Preferences.Add(NotificationPreference.Create(userId, category, inApp, push, _clock.UtcNow));
        }
        else
        {
            preference.Update(inApp, push, _clock.UtcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetPreferencesAsync(userId, cancellationToken);
    }

    public static NotificationDto ToDto(Notification n, bool arabic)
    {
        var title = n.Type;
        var body = string.Empty;
        if (NotificationCatalog.TryGet(n.Type, out var template))
        {
            var data = n.Data;
            title = NotificationCatalog.Render(arabic ? template.TitleAr : template.TitleEn, data);
            body = NotificationCatalog.Render(arabic ? template.BodyAr : template.BodyEn, data);
        }

        return new NotificationDto(n.Id, n.Type, n.Category.ToString(), title, body, n.DeepLink, n.ReadAtUtc is not null, n.CreatedAtUtc);
    }
}
