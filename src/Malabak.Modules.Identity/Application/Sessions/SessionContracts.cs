namespace Malabak.Modules.Identity.Application.Sessions;

/// <summary>Device details of the current request (headers X-Device-Id / X-Device-Name, IP, User-Agent).</summary>
public interface IDeviceContext
{
    string? DeviceId { get; }
    string? DeviceName { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}

/// <summary>Fast "is this session still alive?" check used on every authenticated request.</summary>
public interface ISessionStore
{
    Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Marks sessions revoked and publishes a session-revoked event (e.g. to disconnect WebSockets).</summary>
    Task MarkRevokedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken = default);
}

public sealed record SessionDto(
    Guid SessionId,
    string? DeviceName,
    string? DeviceId,
    string IpAddress,
    string UserAgent,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc,
    bool IsCurrent);

/// <summary>A session was revoked; realtime hubs subscribe to this to drop connections of that session.</summary>
public sealed record SessionRevokedEvent(Guid UserId, Guid SessionId, string Reason);
