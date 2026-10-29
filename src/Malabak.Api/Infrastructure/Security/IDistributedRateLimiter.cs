namespace Malabak.Api.Infrastructure.Security;

public sealed record RateLimitResult(
    bool IsAllowed,
    long Remaining,
    TimeSpan RetryAfter,
    long Limit);

public interface IDistributedRateLimiter
{
    Task<RateLimitResult> CheckSlidingWindowAsync(
        string policy,
        string key,
        int permitLimit,
        TimeSpan window,
        bool isSensitive = false,
        CancellationToken cancellationToken = default);

    Task<RateLimitResult> CheckFixedWindowAsync(
        string policy,
        string key,
        int permitLimit,
        TimeSpan window,
        bool isSensitive = false,
        CancellationToken cancellationToken = default);

    Task<RateLimitResult> CheckTokenBucketAsync(
        string policy,
        string key,
        int tokenLimit,
        int tokensPerPeriod,
        TimeSpan replenishmentPeriod,
        bool isSensitive = false,
        CancellationToken cancellationToken = default);
}
