namespace Malabak.Modules.Venues.Domain;

/// <summary>
/// An area (district) added on top of the built-in catalog, by an admin or by a venue owner who works in a place the
/// catalog does not list. It joins its governorate's area list in the reference data for everyone.
/// </summary>
public sealed class CustomArea
{
    public const int MaxNameLength = 60;

    private CustomArea()
    {
    }

    public Guid Id { get; private set; }
    public string GovernorateCode { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public Guid AddedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>A stable code derived from the id, in the same shape as the built-in area codes.</summary>
    public string Code => $"custom_{Id:N}"[..15];

    public static CustomArea Create(string governorateCode, string nameAr, string? nameEn, Guid addedByUserId, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        GovernorateCode = governorateCode,
        NameAr = nameAr,
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? nameAr : nameEn,
        AddedByUserId = addedByUserId,
        CreatedAtUtc = nowUtc
    };
}
