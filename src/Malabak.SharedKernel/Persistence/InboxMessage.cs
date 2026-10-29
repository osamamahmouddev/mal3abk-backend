namespace Malabak.SharedKernel.Persistence;

public sealed class InboxMessage
{
    public Guid EventId { get; init; }
    public string HandlerName { get; init; } = string.Empty;
    public DateTime ReceivedOnUtc { get; init; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public string? Error { get; private set; }

    public void MarkAsProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        Error = null;
    }

    public void MarkAsFailed(string error)
    {
        Error = error;
    }
}
