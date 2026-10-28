namespace Malabak.SharedKernel;

public interface IDomainEvent
{
    Guid EventId => IdGenerator.NewGuid();
    DateTime OccurredOnUtc => DateTime.UtcNow;
}
