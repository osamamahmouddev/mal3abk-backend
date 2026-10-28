using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Domain;

public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken()
    {
    }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public bool IsRevoked { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;
    public bool IsActive(DateTime nowUtc) => !IsRevoked && !IsExpired(nowUtc);

    public static RefreshToken Create(
        Guid id,
        Guid userId,
        string tokenHash,
        Guid familyId,
        DateTime expiresAtUtc,
        DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new RefreshToken
        {
            Id = id,
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            ExpiresAtUtc = expiresAtUtc,
            IsRevoked = false,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void Revoke(string? replacedByTokenHash, DateTime nowUtc)
    {
        IsRevoked = true;
        ReplacedByTokenHash = replacedByTokenHash;
        UpdatedAtUtc = nowUtc;
    }
}
