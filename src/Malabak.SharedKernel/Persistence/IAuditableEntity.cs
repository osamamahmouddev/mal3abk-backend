namespace Malabak.SharedKernel.Persistence;

public interface IAuditableEntity
{
    DateTime CreatedAtUtc { get; }
    DateTime? UpdatedAtUtc { get; }
    void SetCreatedAt(DateTime createdAtUtc);
    void SetUpdatedAt(DateTime updatedAtUtc);
}
