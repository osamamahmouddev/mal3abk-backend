namespace Malabak.Api.Infrastructure.Security;

public enum RateLimitAlgorithm
{
    SlidingWindow,
    FixedWindow,
    TokenBucket
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class DistributedRateLimitAttribute : Attribute
{
    public string Policy { get; }
    public int PermitLimit { get; init; } = 60;
    public int WindowSeconds { get; init; } = 60;
    public RateLimitAlgorithm Algorithm { get; init; } = RateLimitAlgorithm.SlidingWindow;
    public bool IsSensitive { get; init; }

    public DistributedRateLimitAttribute(string policy)
    {
        Policy = policy;
    }
}
