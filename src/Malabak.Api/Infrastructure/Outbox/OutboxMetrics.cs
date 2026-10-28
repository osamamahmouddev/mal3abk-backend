using System.Diagnostics.Metrics;

namespace Malabak.Api.Infrastructure.Outbox;

public static class OutboxMetrics
{
    public const string MeterName = "Malabak.Outbox";

    private static readonly Meter _meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> _dispatchedCounter = _meter.CreateCounter<long>(
        name: "malabak.outbox.messages.dispatched",
        unit: "messages",
        description: "Total number of outbox messages successfully dispatched");

    private static readonly Counter<long> _failedCounter = _meter.CreateCounter<long>(
        name: "malabak.outbox.messages.failed",
        unit: "messages",
        description: "Total number of outbox messages that failed dispatch");

    private static readonly Histogram<double> _durationHistogram = _meter.CreateHistogram<double>(
        name: "malabak.outbox.dispatch.duration",
        unit: "ms",
        description: "Duration of outbox message dispatch execution in milliseconds");

    public static void RecordDispatched(string eventType)
    {
        _dispatchedCounter.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    }

    public static void RecordFailed(string eventType, string exceptionType, bool isDeadLetter)
    {
        _failedCounter.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("exception_type", exceptionType),
            new KeyValuePair<string, object?>("is_dead_letter", isDeadLetter));
    }

    public static void RecordDuration(double durationMs)
    {
        _durationHistogram.Record(durationMs);
    }
}
