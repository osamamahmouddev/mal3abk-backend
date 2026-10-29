using System.Diagnostics.Metrics;
using Malabak.Api.Infrastructure.Redis;
using Malabak.SharedKernel.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Security;

public sealed class RedisDistributedRateLimiter : IDistributedRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IRedisKeyBuilder _keyBuilder;
    private readonly ILogger<RedisDistributedRateLimiter> _logger;

    private static readonly Meter _meter = new("Malabak.RateLimiting", "1.0.0");
    private static readonly Counter<long> _rejectedCounter = _meter.CreateCounter<long>("malabak.ratelimit.rejected");
    private static readonly Counter<long> _acceptedCounter = _meter.CreateCounter<long>("malabak.ratelimit.accepted");

    private const string _slidingWindowScript = @"
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local member = ARGV[4]

        local clearBefore = now - window
        redis.call('ZREMRANGEBYSCORE', key, 0, clearBefore)
        local currentCount = redis.call('ZCARD', key)

        if currentCount < limit then
            redis.call('ZADD', key, now, member)
            redis.call('PEXPIRE', key, window + 1000)
            return { 1, limit - currentCount - 1, 0 }
        else
            local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
            local retryAfterMs = window
            if oldest and #oldest >= 2 then
                local oldestScore = tonumber(oldest[2])
                retryAfterMs = math.max(0, (oldestScore + window) - now)
            end
            return { 0, 0, math.max(1, math.ceil(retryAfterMs / 1000)) }
        end";

    private const string _fixedWindowScript = @"
        local key = KEYS[1]
        local limit = tonumber(ARGV[1])
        local windowSec = tonumber(ARGV[2])

        local current = redis.call('INCR', key)
        if current == 1 then
            redis.call('EXPIRE', key, windowSec)
        end

        local ttl = redis.call('TTL', key)
        if ttl < 0 then
            ttl = windowSec
        end

        if current <= limit then
            return { 1, limit - current, 0 }
        else
            return { 0, 0, math.max(1, ttl) }
        end";

    private const string _tokenBucketScript = @"
        local key = KEYS[1]
        local capacity = tonumber(ARGV[1])
        local refillRate = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        local requested = 1

        local data = redis.call('HMGET', key, 'tokens', 'lastRefill')
        local tokens = tonumber(data[1])
        local lastRefill = tonumber(data[2])

        if not tokens then
            tokens = capacity
            lastRefill = now
        else
            local elapsed = math.max(0, now - lastRefill)
            tokens = math.min(capacity, tokens + (elapsed * refillRate))
            lastRefill = now
        end

        if tokens >= requested then
            tokens = tokens - requested
            redis.call('HMSET', key, 'tokens', tokens, 'lastRefill', lastRefill)
            local ttlMs = math.ceil(capacity / refillRate)
            redis.call('PEXPIRE', key, math.max(1000, ttlMs))
            return { 1, math.floor(tokens), 0 }
        else
            redis.call('HMSET', key, 'tokens', tokens, 'lastRefill', lastRefill)
            local missing = requested - tokens
            local retryAfterMs = math.ceil(missing / refillRate)
            return { 0, 0, math.max(1, math.ceil(retryAfterMs / 1000)) }
        end";

    public RedisDistributedRateLimiter(
        IConnectionMultiplexer redis,
        IRedisKeyBuilder keyBuilder,
        ILogger<RedisDistributedRateLimiter> logger)
    {
        _redis = redis;
        _keyBuilder = keyBuilder;
        _logger = logger;
    }

    public async Task<RateLimitResult> CheckSlidingWindowAsync(
        string policy,
        string key,
        int permitLimit,
        TimeSpan window,
        bool isSensitive = false,
        CancellationToken cancellationToken = default)
    {
        var redisKey = _keyBuilder.Format("ratelimit", "sliding", policy, key);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = (long)window.TotalMilliseconds;
        var member = $"{nowMs}-{Guid.NewGuid():N}";

        return await ExecuteRateLimitScriptAsync(
            policy,
            redisKey,
            _slidingWindowScript,
            [redisKey],
            [nowMs, windowMs, permitLimit, member],
            permitLimit,
            isSensitive,
            cancellationToken);
    }

    public async Task<RateLimitResult> CheckFixedWindowAsync(
        string policy,
        string key,
        int permitLimit,
        TimeSpan window,
        bool isSensitive = false,
        CancellationToken cancellationToken = default)
    {
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)window.TotalSeconds;
        var redisKey = _keyBuilder.Format("ratelimit", "fixed", policy, key, bucket.ToString());
        var windowSec = Math.Max(1, (int)window.TotalSeconds);

        return await ExecuteRateLimitScriptAsync(
            policy,
            redisKey,
            _fixedWindowScript,
            [redisKey],
            [permitLimit, windowSec],
            permitLimit,
            isSensitive,
            cancellationToken);
    }

    public async Task<RateLimitResult> CheckTokenBucketAsync(
        string policy,
        string key,
        int tokenLimit,
        int tokensPerPeriod,
        TimeSpan replenishmentPeriod,
        bool isSensitive = false,
        CancellationToken cancellationToken = default)
    {
        var redisKey = _keyBuilder.Format("ratelimit", "token", policy, key);
        var refillRatePerMs = (double)tokensPerPeriod / replenishmentPeriod.TotalMilliseconds;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return await ExecuteRateLimitScriptAsync(
            policy,
            redisKey,
            _tokenBucketScript,
            [redisKey],
            [tokenLimit, refillRatePerMs, nowMs],
            tokenLimit,
            isSensitive,
            cancellationToken);
    }

    private async Task<RateLimitResult> ExecuteRateLimitScriptAsync(
        string policy,
        string redisKey,
        string script,
        RedisKey[] keys,
        RedisValue[] values,
        long limit,
        bool isSensitive,
        CancellationToken cancellationToken)
    {
        try
        {
            var db = _redis.GetDatabase();

            RedisResult result;
            if (isSensitive)
            {
                result = await FailClosedPolicy.ExecuteSensitiveAsync(
                    $"RateLimit.{policy}",
                    redisKey,
                    async () => await db.ScriptEvaluateAsync(script, keys, values).ConfigureAwait(false),
                    _logger,
                    cancellationToken);
            }
            else
            {
                result = await FailClosedPolicy.ExecuteResilientAsync(
                    $"RateLimit.{policy}",
                    redisKey,
                    async () => await db.ScriptEvaluateAsync(script, keys, values).ConfigureAwait(false),
                    RedisResult.Create(new RedisValue[] { 1, limit - 1, 0 }),
                    _logger,
                    cancellationToken);
            }

            var results = (RedisValue[]?)result;
            if (results is null || results.Length < 3)
            {
                return new RateLimitResult(true, limit - 1, TimeSpan.Zero, limit);
            }

            var allowed = (int)results[0] == 1;
            var remaining = (long)results[1];
            var retryAfterSec = (long)results[2];

            if (allowed)
            {
                _acceptedCounter.Add(1, new KeyValuePair<string, object?>("policy", policy));
                return new RateLimitResult(true, remaining, TimeSpan.Zero, limit);
            }

            _rejectedCounter.Add(1, new KeyValuePair<string, object?>("policy", policy));
            return new RateLimitResult(false, 0, TimeSpan.FromSeconds(retryAfterSec), limit);
        }
        catch (RedisFailClosedException ex) when (isSensitive)
        {
            _rejectedCounter.Add(1, new KeyValuePair<string, object?>("policy", policy), new KeyValuePair<string, object?>("reason", "fail_closed"));
            _logger.LogError(ex, "Rate limiter rejected sensitive request for policy {Policy} due to Redis fail-closed.", policy);
            return new RateLimitResult(false, 0, TimeSpan.FromSeconds(60), limit);
        }
    }
}
