using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Malabak.Api.Infrastructure.Hangfire;

/// <summary>
/// OpenTelemetry metrics instruments for Hangfire background jobs.
/// </summary>
public static class HangfireMetrics
{
    public const string MeterName = "Malabak.Hangfire";
    private static readonly Meter _meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> _jobsExecutedCounter = _meter.CreateCounter<long>(
        name: "malabak.hangfire.jobs.executed",
        unit: "{jobs}",
        description: "Total number of Hangfire background jobs executed");

    private static readonly Counter<long> _jobsFailedCounter = _meter.CreateCounter<long>(
        name: "malabak.hangfire.jobs.failed",
        unit: "{jobs}",
        description: "Total number of permanently failed Hangfire jobs");

    private static readonly Counter<long> _jobsRetriedCounter = _meter.CreateCounter<long>(
        name: "malabak.hangfire.jobs.retried",
        unit: "{jobs}",
        description: "Total number of retried Hangfire background jobs");

    private static readonly Histogram<double> _jobDurationHistogram = _meter.CreateHistogram<double>(
        name: "malabak.hangfire.jobs.duration_ms",
        unit: "ms",
        description: "Execution duration of Hangfire jobs in milliseconds");

    public static void RecordJobExecuted(string jobType, string queue, string status, double durationMs)
    {
        var tags = new TagList
        {
            { "job_type", jobType },
            { "queue", queue },
            { "status", status }
        };

        _jobsExecutedCounter.Add(1, tags);
        _jobDurationHistogram.Record(durationMs, tags);
    }

    public static void RecordJobFailed(string jobType, string queue, string exceptionType)
    {
        var tags = new TagList
        {
            { "job_type", jobType },
            { "queue", queue },
            { "exception_type", exceptionType }
        };

        _jobsFailedCounter.Add(1, tags);
    }

    public static void RecordJobRetried(string jobType, string queue, int attempt)
    {
        var tags = new TagList
        {
            { "job_type", jobType },
            { "queue", queue },
            { "attempt", attempt }
        };

        _jobsRetriedCounter.Add(1, tags);
    }
}
