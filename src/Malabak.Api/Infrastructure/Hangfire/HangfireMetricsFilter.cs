using System.Diagnostics;
using Hangfire.Common;
using Hangfire.Server;

namespace Malabak.Api.Infrastructure.Hangfire;

/// <summary>
/// Hangfire server filter that measures background job execution duration and records OpenTelemetry metrics.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class HangfireMetricsFilterAttribute : JobFilterAttribute, IServerFilter
{
    private const string _stopwatchParameter = "ExecutionStopwatch";

    public void OnPerforming(PerformingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.SetJobParameter(_stopwatchParameter, Stopwatch.StartNew());
    }

    public void OnPerformed(PerformedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = context.GetJobParameter<Stopwatch>(_stopwatchParameter);
        var elapsedMs = stopwatch?.Elapsed.TotalMilliseconds ?? 0;

        var jobType = context.BackgroundJob.Job?.Type.Name ?? "Unknown";
        var queue = context.BackgroundJob.Job?.Queue ?? HangfireQueues.Default;
        var hasError = context.Exception is not null;
        var status = hasError ? "failed" : "succeeded";

        HangfireMetrics.RecordJobExecuted(jobType, queue, status, elapsedMs);

        if (hasError)
        {
            var exceptionType = context.Exception!.GetType().Name;
            HangfireMetrics.RecordJobFailed(jobType, queue, exceptionType);
        }
    }
}
