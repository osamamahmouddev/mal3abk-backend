namespace Malabak.SharedKernel.Security;

/// <summary>
/// Identifies where an encrypted value lives. It is bound to the ciphertext as AES-GCM associated data,
/// so a ciphertext copied into another row or column fails authentication instead of decrypting.
/// </summary>
public sealed record FieldEncryptionContext
{
    public FieldEncryptionContext(string table, string column, Guid rowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(column);

        if (rowId == Guid.Empty)
        {
            throw new ArgumentException("Row id is required to bind the ciphertext to its row.", nameof(rowId));
        }

        Table = table.Trim().ToLowerInvariant();
        Column = column.Trim().ToLowerInvariant();
        RowId = rowId;
    }

    public string Table { get; }
    public string Column { get; }
    public Guid RowId { get; }

    public string ToAssociatedData() => $"{Table}|{Column}|{RowId:D}";
}
