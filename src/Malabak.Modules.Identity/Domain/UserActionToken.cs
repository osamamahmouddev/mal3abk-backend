namespace Malabak.Modules.Identity.Domain;

public enum UserActionTokenPurpose
{
    EmailVerification = 0,
    PasswordReset = 1
}

/// <summary>
/// Single-use, expiring token for email verification and password reset. Only the SHA-256 hash is stored,
/// so a database leak can't be used to verify emails or reset passwords.
/// </summary>
public sealed class UserActionToken
{
    public static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(30);

    private UserActionToken()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public UserActionTokenPurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    public static UserActionToken Create(Guid userId, UserActionTokenPurpose purpose, string tokenHash, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        var lifetime = purpose == UserActionTokenPurpose.PasswordReset ? PasswordResetLifetime : EmailVerificationLifetime;
        return new UserActionToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Purpose = purpose,
            TokenHash = tokenHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc + lifetime
        };
    }

    public bool IsUsable(DateTime nowUtc) => UsedAtUtc is null && ExpiresAtUtc > nowUtc;

    public void MarkUsed(DateTime nowUtc) => UsedAtUtc = nowUtc;
}
