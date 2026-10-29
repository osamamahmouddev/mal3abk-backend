using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Domain;

/// <summary>
/// One signed-in device. Its id is the access token's <c>sid</c> claim and the refresh-token family id,
/// so revoking the session invalidates both the refresh chain and (on the next request) the access token.
/// </summary>
public sealed class UserSession : Entity<Guid>
{
    public const int MaxDeviceIdLength = 128;
    public const int MaxDeviceNameLength = 100;
    public const int MaxUserAgentLength = 512;

    private UserSession()
    {
    }

    public Guid UserId { get; private set; }
    public string IpAddress { get; private set; } = string.Empty;
    public string UserAgent { get; private set; } = string.Empty;
    public string? DeviceId { get; private set; }
    public string? DeviceName { get; private set; }
    public bool IsAdminSession { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public bool IsRevoked { get; private set; }
    public string? RevokedReason { get; private set; }

    public static UserSession Create(
        Guid id,
        Guid userId,
        string? ipAddress,
        string? userAgent,
        DateTime nowUtc,
        string? deviceId = null,
        string? deviceName = null,
        bool isAdminSession = false)
    {
        return new UserSession
        {
            Id = id,
            UserId = userId,
            IpAddress = Clip(ipAddress, 64) ?? string.Empty,
            UserAgent = Clip(userAgent, MaxUserAgentLength) ?? string.Empty,
            DeviceId = Clip(deviceId, MaxDeviceIdLength),
            DeviceName = Clip(deviceName, MaxDeviceNameLength),
            IsAdminSession = isAdminSession,
            LastActivityAtUtc = nowUtc,
            IsRevoked = false,
            CreatedAtUtc = nowUtc
        };
    }

    public void Touch(DateTime nowUtc)
    {
        LastActivityAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void Revoke(DateTime nowUtc, string? reason = null)
    {
        IsRevoked = true;
        RevokedReason = reason;
        UpdatedAtUtc = nowUtc;
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
