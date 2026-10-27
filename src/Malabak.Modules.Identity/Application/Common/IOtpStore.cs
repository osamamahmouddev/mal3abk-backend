namespace Malabak.Modules.Identity.Application.Common;

public record OtpVerificationResult(
    bool IsSuccess,
    int RemainingAttempts,
    bool IsExpiredOrNotFound = false,
    bool IsMaxAttemptsExceeded = false);

public interface IOtpStore
{
    Task<bool> CheckAndRecordSendAttemptAsync(
        string normalizedPhone,
        int maxAttempts = 3,
        TimeSpan? window = null,
        CancellationToken cancellationToken = default);

    Task SaveOtpAsync(
        string normalizedPhone,
        string hmacHash,
        TimeSpan ttl,
        int maxVerificationAttempts = 5,
        CancellationToken cancellationToken = default);

    Task<OtpVerificationResult> VerifyAndConsumeOtpAsync(
        string normalizedPhone,
        string candidateCode,
        IOtpSecurityService securityService,
        CancellationToken cancellationToken = default);
}
