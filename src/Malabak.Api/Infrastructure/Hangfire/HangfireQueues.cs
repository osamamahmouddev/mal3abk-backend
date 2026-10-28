namespace Malabak.Api.Infrastructure.Hangfire;

/// <summary>
/// Defines the standardized Hangfire named queues in priority order.
/// </summary>
public static class HangfireQueues
{
    /// <summary>
    /// Highest priority queue for time-critical jobs (payment processing, instant OTP/SMS dispatch, booking timeout notifications).
    /// </summary>
    public const string Critical = "critical";

    /// <summary>
    /// Standard priority queue for regular application background jobs (outbox message processing, emails, async domain events).
    /// </summary>
    public const string Default = "default";

    /// <summary>
    /// Lowest priority queue for non-urgent tasks (nightly audit aggregation, expired data retention cleanup, rating recalculation).
    /// </summary>
    public const string Low = "low";

    /// <summary>
    /// All queues ordered by processing priority.
    /// </summary>
    public static readonly string[] OrderedQueues = [Critical, Default, Low];
}
