namespace Malabak.Modules.Identity.Domain;

/// <summary>One-time 2FA recovery code for platform staff; only its SHA-256 hash is stored.</summary>
public sealed class AdminRecoveryCode
{
    public const int CodesPerUser = 10;

    private AdminRecoveryCode()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    public static AdminRecoveryCode Create(Guid userId, string codeHash, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        return new AdminRecoveryCode { Id = Guid.CreateVersion7(), UserId = userId, CodeHash = codeHash, CreatedAtUtc = nowUtc };
    }

    public void MarkUsed(DateTime nowUtc) => UsedAtUtc = nowUtc;
}
