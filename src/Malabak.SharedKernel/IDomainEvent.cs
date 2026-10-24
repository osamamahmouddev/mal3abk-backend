namespace Malabak.SharedKernel;

public interface IDomainEvent
{
    Guid EventId => Guid.NewGuid();
    DateTime OccurredOnUtc => DateTime.UtcNow;
}
