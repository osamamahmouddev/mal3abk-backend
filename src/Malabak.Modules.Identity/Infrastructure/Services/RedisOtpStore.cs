using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class RedisOtpStore : IOtpStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisOtpStore> _logger;

    public RedisOtpStore(IConnectionMultiplexer redis, ILogger<RedisOtpStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<bool> CheckAndRecordSendAttemptAsync(
        string normalizedPhone,
        int maxAttempts = 3,
        TimeSpan? window = null,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"ratelimit:otp:send:{normalizedPhone}";
        var windowDuration = window ?? TimeSpan.FromMinutes(10);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thresholdMs = nowMs - (long)windowDuration.TotalMilliseconds;

        // Clean up expired attempts outside window
        await db.SortedSetRemoveRangeByScoreAsync(key, 0, thresholdMs);

        // Count attempts in current window
        var count = await db.SortedSetLengthAsync(key);
        if (count >= maxAttempts)
        {
            _logger.LogWarning("Rate limit exceeded for phone {Phone}: {Count}/{Max} attempts", normalizedPhone, count, maxAttempts);
            return false;
        }

        // Record attempt with unique member and timestamp score
        var member = $"{nowMs}:{Guid.NewGuid():N}";
        await db.SortedSetAddAsync(key, member, nowMs);
        await db.KeyExpireAsync(key, windowDuration);

        return true;
    }

    public async Task SaveOtpAsync(
        string normalizedPhone,
        string hmacHash,
        TimeSpan ttl,
        int maxVerificationAttempts = 5,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"otp:{normalizedPhone}";

        HashEntry[] entries =
        [
            new HashEntry("hash", hmacHash),
            new HashEntry("attempts_left", maxVerificationAttempts)
        ];

        await db.HashSetAsync(key, entries);
        await db.KeyExpireAsync(key, ttl);
    }

    public async Task<OtpVerificationResult> VerifyAndConsumeOtpAsync(
        string normalizedPhone,
        string candidateCode,
        IOtpSecurityService securityService,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"otp:{normalizedPhone}";

        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0)
        {
            return new OtpVerificationResult(false, 0, IsExpiredOrNotFound: true);
        }

        var entryDict = entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        if (!entryDict.TryGetValue("hash", out var storedHash) ||
            !entryDict.TryGetValue("attempts_left", out var attemptsStr) ||
            !int.TryParse(attemptsStr, out var attemptsLeft))
        {
            await db.KeyDeleteAsync(key);
            return new OtpVerificationResult(false, 0, IsExpiredOrNotFound: true);
        }

        if (attemptsLeft <= 0)
        {
            await db.KeyDeleteAsync(key);
            return new OtpVerificationResult(false, 0, IsMaxAttemptsExceeded: true);
        }

        attemptsLeft--;
        var isMatch = securityService.VerifyCode(candidateCode, storedHash);

        if (isMatch)
        {
            await db.KeyDeleteAsync(key);
            return new OtpVerificationResult(true, attemptsLeft);
        }

        if (attemptsLeft <= 0)
        {
            await db.KeyDeleteAsync(key);
            return new OtpVerificationResult(false, 0, IsMaxAttemptsExceeded: true);
        }

        await db.HashSetAsync(key, "attempts_left", attemptsLeft);
        return new OtpVerificationResult(false, attemptsLeft);
    }
}
