using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Domain;

public sealed class UserSession : Entity<Guid>
{
    private UserSession()
    {
    }

    public Guid UserId { get; private set; }
    public string IpAddress { get; private set; } = string.Empty;
    public string UserAgent { get; private set; } = string.Empty;
    public DateTime LastActivityAtUtc { get; private set; }
    public bool IsRevoked { get; private set; }

    public static UserSession Create(
        Guid id,
        Guid userId,
        string ipAddress,
        string userAgent,
        DateTime nowUtc)
    {
        return new UserSession
        {
            Id = id,
            UserId = userId,
            IpAddress = ipAddress.Trim(),
            UserAgent = userAgent.Trim(),
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

    public void Revoke(DateTime nowUtc)
    {
        IsRevoked = true;
        UpdatedAtUtc = nowUtc;
    }
}
