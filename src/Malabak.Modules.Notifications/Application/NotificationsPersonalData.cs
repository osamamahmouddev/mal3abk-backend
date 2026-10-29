using Malabak.Modules.Notifications.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Notifications.Application;

/// <summary>Notification history, preferences and registered devices (never the push token itself).</summary>
public sealed class NotificationsPersonalData : IPersonalDataContributor
{
    private readonly NotificationsDbContext _dbContext;

    public NotificationsPersonalData(NotificationsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Section => "notifications";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var notifications = await _dbContext.Notifications.AsNoTracking().Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new { n.Type, Category = n.Category.ToString(), n.CreatedAtUtc, n.ReadAtUtc })
            .ToListAsync(cancellationToken);
        var preferences = await _dbContext.Preferences.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new { Category = p.Category.ToString(), p.InAppEnabled })
            .ToListAsync(cancellationToken);
        var devices = await _dbContext.DeviceTokens.AsNoTracking().Where(d => d.UserId == userId)
            .Select(d => new { Platform = d.Platform.ToString(), d.Language, d.IsActive, d.CreatedAtUtc, d.LastSeenAtUtc })
            .ToListAsync(cancellationToken);
        return new { Notifications = notifications, Preferences = preferences, Devices = devices };
    }

    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _dbContext.DeviceTokens.RemoveRange(await _dbContext.DeviceTokens.Where(d => d.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.Notifications.RemoveRange(await _dbContext.Notifications.Where(n => n.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.Preferences.RemoveRange(await _dbContext.Preferences.Where(p => p.UserId == userId).ToListAsync(cancellationToken));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
