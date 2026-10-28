using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Sessions;

/// <summary>Creates, lists and revokes device sessions, and alerts users about sign-ins from new devices.</summary>
public sealed class SessionService
{
    private readonly IdentityDbContext _dbContext;
    private readonly IDeviceContext _device;
    private readonly ISessionStore _store;
    private readonly ISmsAlertService _sms;
    private readonly IEmailSender _email;
    private readonly IClock _clock;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        IdentityDbContext dbContext,
        IDeviceContext device,
        ISessionStore store,
        ISmsAlertService sms,
        IEmailSender email,
        IClock clock,
        ILogger<SessionService> logger)
    {
        _dbContext = dbContext;
        _device = device;
        _store = store;
        _sms = sms;
        _email = email;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Starts a session for a successful sign-in; the returned id is the token <c>sid</c> and refresh family.</summary>
    public async Task<Guid> StartAsync(User user, bool isAdminSession, CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var isNewDevice = await IsNewDeviceAsync(user.Id, cancellationToken);

        var session = UserSession.Create(
            Guid.CreateVersion7(), user.Id, _device.IpAddress, _device.UserAgent, nowUtc, _device.DeviceId, _device.DeviceName, isAdminSession);
        _dbContext.UserSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (isNewDevice)
        {
            await AlertNewDeviceAsync(user, session, cancellationToken);
        }

        return session.Id;
    }

    /// <summary>Refresh keeps the session alive; returns false when it was revoked (the refresh must then fail).</summary>
    public async Task<bool> TouchAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null || session.IsRevoked)
        {
            return false;
        }

        session.Touch(_clock.UtcNow);
        return true;
    }

    public async Task<IReadOnlyList<SessionDto>> ListAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken = default)
    {
        var sessions = await _dbContext.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .OrderByDescending(s => s.LastActivityAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        return sessions
            .Select(s => new SessionDto(s.Id, s.DeviceName, s.DeviceId, s.IpAddress, s.UserAgent, s.CreatedAtUtc, s.LastActivityAtUtc, s.Id == currentSessionId))
            .ToList();
    }

    /// <summary>Revokes one of the user's sessions. False when it isn't theirs or doesn't exist (no cross-user revocation).</summary>
    public async Task<bool> RevokeAsync(Guid userId, Guid sessionId, string reason, CancellationToken cancellationToken = default)
    {
        var sessions = await _dbContext.UserSessions
            .Where(s => s.Id == sessionId && s.UserId == userId && !s.IsRevoked)
            .ToListAsync(cancellationToken);

        return await RevokeSessionsAsync(sessions, reason, cancellationToken) > 0;
    }

    /// <summary>"Sign out everywhere" (optionally keeping the current device). Returns how many were revoked.</summary>
    public async Task<int> RevokeAllAsync(Guid userId, Guid? exceptSessionId, string reason, CancellationToken cancellationToken = default)
    {
        var sessions = await _dbContext.UserSessions
            .Where(s => s.UserId == userId && !s.IsRevoked && (exceptSessionId == null || s.Id != exceptSessionId))
            .ToListAsync(cancellationToken);

        return await RevokeSessionsAsync(sessions, reason, cancellationToken);
    }

    private async Task<int> RevokeSessionsAsync(List<UserSession> sessions, string reason, CancellationToken cancellationToken)
    {
        if (sessions.Count == 0)
        {
            return 0;
        }

        var nowUtc = _clock.UtcNow;
        var ids = sessions.Select(s => s.Id).ToList();
        sessions.ForEach(s => s.Revoke(nowUtc, reason));

        var refreshTokens = await _dbContext.RefreshTokens.Where(t => ids.Contains(t.FamilyId) && !t.IsRevoked).ToListAsync(cancellationToken);
        refreshTokens.ForEach(t => t.Revoke(reason, nowUtc));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _store.MarkRevokedAsync(ids, cancellationToken);

        _logger.LogInformation("AUDIT: revoked {Count} sessions for user {UserId} ({Reason})", ids.Count, sessions[0].UserId, reason);
        return ids.Count;
    }

    private async Task<bool> IsNewDeviceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var hasPreviousSessions = await _dbContext.UserSessions.AnyAsync(s => s.UserId == userId, cancellationToken);
        if (!hasPreviousSessions)
        {
            return false; // first ever sign-in isn't "suspicious"
        }

        // Without a device id we fall back to the user agent so a different app/browser still counts as new.
        return _device.DeviceId is { } deviceId
            ? !await _dbContext.UserSessions.AnyAsync(s => s.UserId == userId && s.DeviceId == deviceId, cancellationToken)
            : !await _dbContext.UserSessions.AnyAsync(s => s.UserId == userId && s.UserAgent == (_device.UserAgent ?? string.Empty), cancellationToken);
    }

    private async Task AlertNewDeviceAsync(User user, UserSession session, CancellationToken cancellationToken)
    {
        var device = session.DeviceName ?? "a new device";
        var message = $"New sign-in to your Malabak account from {device} at {session.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC. " +
                      "If this wasn't you, sign out all devices from your profile and contact support.";
        try
        {
            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                await _email.SendAsync(user.Email, EmailTemplates.NewDeviceSignIn(device, session.CreatedAtUtc), cancellationToken);
            }
            else
            {
                await _sms.SendSecurityAlertAsync(user.PhoneNumber, message, cancellationToken);
            }

            _logger.LogInformation("AUDIT: new-device sign-in alert sent for user {UserId} (session {SessionId})", user.Id, session.Id);
        }
        catch (Exception ex) when (ex is System.Net.Mail.SmtpException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            // The sign-in itself must not fail because a notification channel is down.
            _logger.LogError(ex, "Failed to send new-device alert for user {UserId}", user.Id);
        }
    }
}
