namespace Malabak.Modules.Venues.Domain;

/// <summary>
/// An area (district or city) within an Egyptian governorate. Standard areas are seeded from the reference catalog;
/// custom areas can be added by admins or venue owners. Admins can pause an area (IsActive) or set it as an operational focus zone (IsFocus).
/// </summary>
public sealed class CustomArea
{
    public const int MaxNameLength = 60;

    private CustomArea()
    {
    }

    public Guid Id { get; private set; }
    public string GovernorateCode { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public bool IsFocus { get; private set; }
    public bool IsCustom { get; private set; }
    public int DisplayOrder { get; private set; }
    public Guid? AddedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static CustomArea CreateStandard(
        string governorateCode,
        string code,
        string nameAr,
        string nameEn,
        int displayOrder,
        DateTime nowUtc) => new()
        {
            Id = Guid.NewGuid(),
            GovernorateCode = governorateCode.ToLowerInvariant().Trim(),
            Code = code.ToLowerInvariant().Trim(),
            NameAr = nameAr.Trim(),
            NameEn = string.IsNullOrWhiteSpace(nameEn) ? nameAr.Trim() : nameEn.Trim(),
            IsActive = true,
            IsFocus = false,
            IsCustom = false,
            DisplayOrder = displayOrder,
            AddedByUserId = null,
            CreatedAtUtc = nowUtc
        };

    public static CustomArea Create(
        string governorateCode,
        string nameAr,
        string? nameEn,
        Guid? addedByUserId,
        DateTime nowUtc,
        bool isFocus = false)
    {
        var id = Guid.NewGuid();
        return new()
        {
            Id = id,
            GovernorateCode = governorateCode.ToLowerInvariant().Trim(),
            Code = $"custom_{id:N}"[..15],
            NameAr = nameAr.Trim(),
            NameEn = string.IsNullOrWhiteSpace(nameEn) ? nameAr.Trim() : nameEn.Trim(),
            IsActive = true,
            IsFocus = isFocus,
            IsCustom = true,
            DisplayOrder = 999,
            AddedByUserId = addedByUserId,
            CreatedAtUtc = nowUtc
        };
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void SetFocus(bool isFocus) => IsFocus = isFocus;
}
