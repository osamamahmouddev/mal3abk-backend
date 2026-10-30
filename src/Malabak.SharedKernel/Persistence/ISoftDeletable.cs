namespace Malabak.SharedKernel.Persistence;

public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTime? DeletedAtUtc { get; }
    void SoftDelete(DateTime deletedAtUtc);
    void Restore();
}
