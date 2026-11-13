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
    public int AttemptsOld { get; private set; }
    public int AttemptsNew { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>Wrong codes allowed per phone number before the request is locked (SECURITY.md §4.3).</summary>
    public const int MaxAttempts = 5;

    public bool IsOpen => Status is PhoneChangeStatus.Started or PhoneChangeStatus.OldVerified;

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

    /// <summary>Counts a wrong old-phone code. Returns true (and fails the request) once the limit is reached.</summary>
    public bool RegisterFailedOldAttempt(DateTime nowUtc)
    {
        AttemptsOld++;
        return FailIfExhausted(AttemptsOld, nowUtc);
    }

    /// <summary>Counts a wrong new-phone code. Returns true (and fails the request) once the limit is reached.</summary>
    public bool RegisterFailedNewAttempt(DateTime nowUtc)
    {
        AttemptsNew++;
        return FailIfExhausted(AttemptsNew, nowUtc);
    }

    public void Fail(DateTime nowUtc)
    {
        OldPhoneOtpHash = null;
        NewPhoneOtpHash = null;
        Status = PhoneChangeStatus.Failed;
        UpdatedAtUtc = nowUtc;
    }

    public void Cancel(DateTime nowUtc)
    {
        OldPhoneOtpHash = null;
        NewPhoneOtpHash = null;
        Status = PhoneChangeStatus.Cancelled;
        UpdatedAtUtc = nowUtc;
    }

    private bool FailIfExhausted(int attempts, DateTime nowUtc)
    {
        if (attempts < MaxAttempts)
        {
            UpdatedAtUtc = nowUtc;
            return false;
        }

        Fail(nowUtc);
        return true;
    }

    public void Expire(DateTime nowUtc)
    {
        Status = PhoneChangeStatus.Expired;
        UpdatedAtUtc = nowUtc;
    }
}
