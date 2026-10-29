namespace Malabak.SharedKernel.Contracts;

/// <summary>
/// Each module that keeps data about a user implements this so account export and deletion cover every module
/// (Law 151/2020 rights of access and erasure) without Identity referencing the others.
/// </summary>
public interface IPersonalDataContributor
{
    /// <summary>Section name in the export, e.g. "favorites".</summary>
    string Section { get; }

    /// <summary>JSON-serializable copy of the user's data; never secrets such as tokens or hashes.</summary>
    Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Deletes personal data that need not be kept; records required for accounting stay, keyed only by id.</summary>
    Task EraseAsync(Guid userId, CancellationToken cancellationToken = default);
}
