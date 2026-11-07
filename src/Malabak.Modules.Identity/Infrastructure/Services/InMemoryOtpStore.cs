using System.Collections.Concurrent;
using Malabak.Modules.Identity.Application.Common;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class InMemoryOtpStore : IOtpStore
{
    private readonly ConcurrentDictionary<string, List<DateTime>> _rateLimitBuckets = new();
    private readonly ConcurrentDictionary<string, OtpRecord> _otpRecords = new();
    private readonly object _syncLock = new();

    public Task<bool> CheckAndRecordSendAttemptAsync(
        string normalizedPhone,
        int maxAttempts = 3,
        TimeSpan? window = null,
        CancellationToken cancellationToken = default)
    {
        var windowDuration = window ?? TimeSpan.FromMinutes(10);
        var now = DateTime.UtcNow;
        var threshold = now - windowDuration;

        lock (_syncLock)
        {
            var attempts = _rateLimitBuckets.GetOrAdd(normalizedPhone, _ => []);
            attempts.RemoveAll(t => t < threshold);

            if (attempts.Count >= maxAttempts)
            {
                return Task.FromResult(false);
            }

            attempts.Add(now);
            return Task.FromResult(true);
        }
    }

    public Task<TimeSpan> GetSendRetryAfterAsync(
        string normalizedPhone,
        TimeSpan? window = null,
        CancellationToken cancellationToken = default)
    {
        var windowDuration = window ?? TimeSpan.FromMinutes(10);
        var now = DateTime.UtcNow;

        lock (_syncLock)
        {
            if (!_rateLimitBuckets.TryGetValue(normalizedPhone, out var attempts) || attempts.Count == 0)
            {
                return Task.FromResult(TimeSpan.Zero);
            }

            var wait = attempts.Min() + windowDuration - now;
            return Task.FromResult(wait > TimeSpan.Zero ? wait : TimeSpan.Zero);
        }
    }

    public Task SaveOtpAsync(
        string normalizedPhone,
        string hmacHash,
        TimeSpan ttl,
        int maxVerificationAttempts = 5,
        CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTime.UtcNow.Add(ttl);
        _otpRecords[normalizedPhone] = new OtpRecord(hmacHash, maxVerificationAttempts, expiresAt);
        return Task.CompletedTask;
    }

    public Task<OtpVerificationResult> VerifyAndConsumeOtpAsync(
        string normalizedPhone,
        string candidateCode,
        IOtpSecurityService securityService,
        CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            if (!_otpRecords.TryGetValue(normalizedPhone, out var record) || DateTime.UtcNow > record.ExpiresAt)
            {
                _otpRecords.TryRemove(normalizedPhone, out _);
                return Task.FromResult(new OtpVerificationResult(false, 0, IsExpiredOrNotFound: true));
            }

            if (record.AttemptsLeft <= 0)
            {
                _otpRecords.TryRemove(normalizedPhone, out _);
                return Task.FromResult(new OtpVerificationResult(false, 0, IsMaxAttemptsExceeded: true));
            }

            var attemptsLeft = record.AttemptsLeft - 1;
            var isMatch = securityService.VerifyCode(candidateCode, record.HmacHash);

            if (isMatch)
            {
                _otpRecords.TryRemove(normalizedPhone, out _);
                return Task.FromResult(new OtpVerificationResult(true, attemptsLeft));
            }

            if (attemptsLeft <= 0)
            {
                _otpRecords.TryRemove(normalizedPhone, out _);
                return Task.FromResult(new OtpVerificationResult(false, 0, IsMaxAttemptsExceeded: true));
            }

            _otpRecords[normalizedPhone] = record with { AttemptsLeft = attemptsLeft };
            return Task.FromResult(new OtpVerificationResult(false, attemptsLeft));
        }
    }

    private sealed record OtpRecord(string HmacHash, int AttemptsLeft, DateTime ExpiresAt);
}
