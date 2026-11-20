using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;

namespace Malabak.Modules.Venues.Domain;

public enum RefundEligibility
{
    FullRefund = 0,
    PartialRefund = 1,
    NoRefund = 2
}

public enum NoShowRule
{
    ForfeitDeposit = 0,
    ForfeitFullAmount = 1,
    NoPenalty = 2
}

/// <summary>Validated values of a booking policy, shared by creation and new versions.</summary>
public sealed record BookingPolicyTerms(
    DepositRule Deposit,
    int ProofSubmissionWindowMinutes,
    int ManualReviewWindowMinutes,
    int CancellationDeadlineHours,
    RefundEligibility RefundEligibility,
    decimal RefundPercentage,
    NoShowRule NoShowRule,
    int LateArrivalGraceMinutes,
    bool AllowReschedule,
    int RescheduleDeadlineHours,
    bool PendingBookingsBlockSlots,
    string? Terms)
{
    public const int MaxTermsLength = 4000;

    /// <summary>
    /// The deposit a venue starts with until the platform team sets its own (commission model: the deposit is paid to the
    /// platform, which sets it per venue). The pricing never lets it fall below the commission.
    /// </summary>
    public static DepositRule DefaultDeposit => DepositRule.Create(DepositType.Percentage, 33);

    /// <summary>The terms a venue has before it publishes its first policy.</summary>
    public static BookingPolicyTerms Default => new(DefaultDeposit, 30, 60, 24, RefundEligibility.FullRefund, 100m,
        NoShowRule.ForfeitDeposit, 15, true, 24, true, null);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        Check(errors, ProofSubmissionWindowMinutes is >= 10 and <= 1440, "Proof submission window must be 10–1440 minutes.");
        Check(errors, ManualReviewWindowMinutes is >= 15 and <= 2880, "Manual review window must be 15–2880 minutes.");
        Check(errors, CancellationDeadlineHours is >= 0 and <= 168, "Cancellation deadline must be 0–168 hours before kickoff.");
        Check(errors, Enum.IsDefined(RefundEligibility), "Unknown refund eligibility.");
        Check(errors, RefundEligibility != RefundEligibility.PartialRefund || RefundPercentage is > 0 and < 100,
            "Partial refund percentage must be between 1 and 99.");
        Check(errors, Enum.IsDefined(NoShowRule), "Unknown no-show rule.");
        Check(errors, LateArrivalGraceMinutes is >= 0 and <= 60, "Late arrival grace must be 0–60 minutes.");
        Check(errors, !AllowReschedule || RescheduleDeadlineHours is >= 0 and <= 168, "Reschedule deadline must be 0–168 hours.");
        Check(errors, (Terms?.Trim().Length ?? 0) <= MaxTermsLength, "Terms must be at most 4000 characters.");
        return errors;
    }

    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}

/// <summary>
/// One immutable version of a venue's booking policy. Changing a policy creates a new version and retires
/// the previous one, so bookings that snapshotted an older version keep the rules they were made under.
/// </summary>
public sealed class VenuePolicy : Entity<Guid>
{
    private VenuePolicy()
    {
    }

    public Guid VenueId { get; private set; }
    public int Version { get; private set; }
    public bool IsCurrent { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? SupersededAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DepositType DepositType { get; private set; }
    public decimal DepositValue { get; private set; }
    public int ProofSubmissionWindowMinutes { get; private set; }
    public int ManualReviewWindowMinutes { get; private set; }
    public int CancellationDeadlineHours { get; private set; }
    public RefundEligibility RefundEligibility { get; private set; }
    public decimal RefundPercentage { get; private set; }
    public NoShowRule NoShowRule { get; private set; }
    public int LateArrivalGraceMinutes { get; private set; }
    public bool AllowReschedule { get; private set; }
    public int RescheduleDeadlineHours { get; private set; }
    public bool PendingBookingsBlockSlots { get; private set; }
    public string? Terms { get; private set; }

    /// <param name="previous">The current version being replaced, or null for the venue's first policy.</param>
    public static VenuePolicy CreateVersion(Guid venueId, VenuePolicy? previous, BookingPolicyTerms terms, Guid userId, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var errors = terms.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(terms));
        }

        if (previous is not null)
        {
            if (previous.VenueId != venueId || !previous.IsCurrent)
            {
                throw new InvalidOperationException("Only the venue's current policy can be superseded.");
            }

            previous.IsCurrent = false;
            previous.SupersededAtUtc = nowUtc;
            previous.UpdatedAtUtc = nowUtc;
        }

        return new VenuePolicy
        {
            Id = Guid.CreateVersion7(),
            VenueId = venueId,
            Version = (previous?.Version ?? 0) + 1,
            IsCurrent = true,
            EffectiveFromUtc = nowUtc,
            CreatedByUserId = userId,
            CreatedAtUtc = nowUtc,
            DepositType = terms.Deposit.Type,
            DepositValue = terms.Deposit.Value,
            ProofSubmissionWindowMinutes = terms.ProofSubmissionWindowMinutes,
            ManualReviewWindowMinutes = terms.ManualReviewWindowMinutes,
            CancellationDeadlineHours = terms.CancellationDeadlineHours,
            RefundEligibility = terms.RefundEligibility,
            RefundPercentage = terms.RefundEligibility switch
            {
                RefundEligibility.FullRefund => 100m,
                RefundEligibility.NoRefund => 0m,
                _ => terms.RefundPercentage
            },
            NoShowRule = terms.NoShowRule,
            LateArrivalGraceMinutes = terms.LateArrivalGraceMinutes,
            AllowReschedule = terms.AllowReschedule,
            RescheduleDeadlineHours = terms.AllowReschedule ? terms.RescheduleDeadlineHours : 0,
            PendingBookingsBlockSlots = terms.PendingBookingsBlockSlots,
            Terms = string.IsNullOrWhiteSpace(terms.Terms) ? null : terms.Terms.Trim()
        };
    }

    public DepositRule ToDepositRule() => DepositRule.Create(DepositType, DepositValue);

    /// <summary>This version's terms, to publish a new version that changes only part of them.</summary>
    public BookingPolicyTerms ToTerms() => new(ToDepositRule(), ProofSubmissionWindowMinutes, ManualReviewWindowMinutes,
        CancellationDeadlineHours, RefundEligibility, RefundPercentage, NoShowRule, LateArrivalGraceMinutes, AllowReschedule,
        RescheduleDeadlineHours, PendingBookingsBlockSlots, Terms);

    public BookingPolicySnapshot ToSnapshot() => new(
        Id, VenueId, Version, DepositType.ToString(), DepositValue, ProofSubmissionWindowMinutes, ManualReviewWindowMinutes,
        CancellationDeadlineHours, RefundEligibility.ToString(), RefundPercentage, NoShowRule.ToString(), LateArrivalGraceMinutes,
        AllowReschedule, RescheduleDeadlineHours, PendingBookingsBlockSlots, Terms, EffectiveFromUtc);
}
