namespace Malabak.Modules.Identity.Domain;

public enum UserActionTokenPurpose
{
    EmailVerification = 0,
    PasswordReset = 1,

    /// <summary>A code sent to the new address before an account's email changes; carries that address.</summary>
    EmailChange = 2
}

/// <summary>
/// Single-use, expiring token for email verification and password reset. Only the SHA-256 hash is stored,
/// so a database leak can't be used to verify emails or reset passwords.
/// </summary>
public sealed class UserActionToken
{
    public static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan EmailChangeLifetime = TimeSpan.FromMinutes(15);
    public const int MaxEmailChangeAttempts = 5;
    public const int MaxEmailLength = 256;

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

    /// <summary>For an email change: the address the account moves to once the code sent there is entered.</summary>
    public string? NewEmail { get; private set; }

    public int FailedAttempts { get; private set; }

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

    /// <summary>A pending email change; <paramref name="tokenHash"/> is the keyed hash of the code bound to <paramref name="id"/>.</summary>
    public static UserActionToken CreateEmailChange(Guid id, Guid userId, string newEmail, string tokenHash, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new UserActionToken
        {
            Id = id,
            UserId = userId,
            Purpose = UserActionTokenPurpose.EmailChange,
            TokenHash = tokenHash,
            NewEmail = newEmail,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc + EmailChangeLifetime
        };
    }

    public bool IsUsable(DateTime nowUtc) => UsedAtUtc is null && ExpiresAtUtc > nowUtc;

    /// <summary>Counts a wrong code; after <see cref="MaxEmailChangeAttempts"/> the token is spent. Returns true when it is.</summary>
    public bool RegisterFailedAttempt(DateTime nowUtc)
    {
        FailedAttempts++;
        if (FailedAttempts >= MaxEmailChangeAttempts)
        {
            UsedAtUtc = nowUtc;
            return true;
        }

        return false;
    }

    public void MarkUsed(DateTime nowUtc) => UsedAtUtc = nowUtc;
}
