using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Domain;

public sealed class PhoneChangeRequest : Entity<Guid>
{
    private PhoneChangeRequest()
    {
    }

    public Guid UserId { get; private set; }
    public string OldPhoneNumber { get; private set; } = string.Empty;
    public string NewPhoneNumber { get; private set; } = string.Empty;
    public PhoneChangeStatus Status { get; private set; }
    public string? OldPhoneOtpHash { get; private set; }
    public string? NewPhoneOtpHash { get; private set; }
    public string? RecoveryTokenHash { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static PhoneChangeRequest Start(
        Guid id,
        Guid userId,
        string oldPhoneNumber,
        string newPhoneNumber,
        string oldPhoneOtpHash,
        DateTime expiresAtUtc,
        DateTime nowUtc)
    {
        return new PhoneChangeRequest
        {
            Id = id,
            UserId = userId,
            OldPhoneNumber = oldPhoneNumber.Trim(),
            NewPhoneNumber = newPhoneNumber.Trim(),
            OldPhoneOtpHash = oldPhoneOtpHash,
            Status = PhoneChangeStatus.Started,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = nowUtc
        };
    }

    public void MarkOldPhoneVerified(string newPhoneOtpHash, DateTime nowUtc)
    {
        OldPhoneOtpHash = null;
        NewPhoneOtpHash = newPhoneOtpHash;
        Status = PhoneChangeStatus.OldVerified;
        UpdatedAtUtc = nowUtc;
    }

    public void Complete(string recoveryTokenHash, DateTime nowUtc)
    {
        NewPhoneOtpHash = null;
        RecoveryTokenHash = recoveryTokenHash;
        Status = PhoneChangeStatus.Completed;
        CompletedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void Expire(DateTime nowUtc)
    {
        Status = PhoneChangeStatus.Expired;
        UpdatedAtUtc = nowUtc;
    }
}
