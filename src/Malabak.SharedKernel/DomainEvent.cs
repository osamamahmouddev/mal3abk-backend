namespace Malabak.SharedKernel;

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = IdGenerator.NewGuid();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
