using Malabak.SharedKernel.Persistence;

namespace Malabak.SharedKernel;

public abstract class Entity<TId> : IAuditableEntity, ISoftDeletable, IVersionedEntity
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public TId Id { get; protected set; } = default!;
    public DateTime CreatedAtUtc { get; protected set; }
    public DateTime? UpdatedAtUtc { get; protected set; }
    public uint RowVersion { get; protected set; }
    public bool IsDeleted { get; protected set; }
    public DateTime? DeletedAtUtc { get; protected set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    public void SetCreatedAt(DateTime createdAtUtc)
    {
        CreatedAtUtc = createdAtUtc;
    }

    public void SetUpdatedAt(DateTime updatedAtUtc)
    {
        UpdatedAtUtc = updatedAtUtc;
    }

    public void SoftDelete(DateTime deletedAtUtc)
    {
        IsDeleted = true;
        DeletedAtUtc = deletedAtUtc;
    }

    public void Restore()
    {
        IsDeleted = false;
        DeletedAtUtc = null;
    }

    public void SetRowVersion(uint version)
    {
        RowVersion = version;
    }
}
