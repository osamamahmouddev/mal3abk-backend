namespace Malabak.SharedKernel;

public interface IOutboxMessage
{
    Guid Id { get; }
    string Type { get; }
    string Content { get; }
    DateTime OccurredOnUtc { get; }
    DateTime? ProcessedOnUtc { get; }
    string? Error { get; }
}
