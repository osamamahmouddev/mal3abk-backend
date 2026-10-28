using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public sealed class VenuePolicy : Entity<Guid>
{
    private VenuePolicy()
    {
    }

    public Guid VenueId { get; private set; }
    public decimal DepositPercentage { get; private set; }
    public int CancellationWindowHours { get; private set; }
    public string RefundPolicyType { get; private set; } = "FullRefund";
    public string? TermsAndConditions { get; private set; }

    public static VenuePolicy Create(
        Guid id,
        Guid venueId,
        decimal depositPercentage,
        int cancellationWindowHours,
        string refundPolicyType,
        string? termsAndConditions,
        DateTime createdAtUtc)
    {
        return new VenuePolicy
        {
            Id = id,
            VenueId = venueId,
            DepositPercentage = depositPercentage,
            CancellationWindowHours = cancellationWindowHours,
            RefundPolicyType = refundPolicyType.Trim(),
            TermsAndConditions = termsAndConditions?.Trim(),
            CreatedAtUtc = createdAtUtc
        };
    }

    public void Update(
        decimal depositPercentage,
        int cancellationWindowHours,
        string refundPolicyType,
        string? termsAndConditions,
        DateTime nowUtc)
    {
        DepositPercentage = depositPercentage;
        CancellationWindowHours = cancellationWindowHours;
        RefundPolicyType = refundPolicyType.Trim();
        TermsAndConditions = termsAndConditions?.Trim();
        UpdatedAtUtc = nowUtc;
    }
}
