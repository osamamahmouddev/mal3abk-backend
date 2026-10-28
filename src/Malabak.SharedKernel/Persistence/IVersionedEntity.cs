namespace Malabak.SharedKernel.Persistence;

public interface IVersionedEntity
{
    uint RowVersion { get; }
    void SetRowVersion(uint version);
}
