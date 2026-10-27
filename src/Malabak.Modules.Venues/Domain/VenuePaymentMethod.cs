using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public sealed class VenuePaymentMethod : Entity<Guid>
{
    private VenuePaymentMethod()
    {
    }

    public Guid VenueId { get; private set; }
    public string MethodType { get; private set; } = string.Empty;
    public string AccountIdentifier { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public string? Instructions { get; private set; }
    public bool IsActive { get; private set; }

    public static VenuePaymentMethod Create(
        Guid id,
        Guid venueId,
        string methodType,
        string accountIdentifier,
        string accountName,
        string? instructions,
        DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodType);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        return new VenuePaymentMethod
        {
            Id = id,
            VenueId = venueId,
            MethodType = methodType.Trim(),
            AccountIdentifier = accountIdentifier.Trim(),
            AccountName = accountName.Trim(),
            Instructions = instructions?.Trim(),
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void Update(
        string methodType,
        string accountIdentifier,
        string accountName,
        string? instructions,
        bool isActive,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodType);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        MethodType = methodType.Trim();
        AccountIdentifier = accountIdentifier.Trim();
        AccountName = accountName.Trim();
        Instructions = instructions?.Trim();
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
    }
}
