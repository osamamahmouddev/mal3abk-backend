using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Domain;

/// <summary>
/// Money the platform sent to a venue (commission model): deposits are paid to the platform, which keeps its commission and
/// transfers the rest. A payout is a financial record: it is never edited or deleted.
/// </summary>
public sealed class VenuePayout : Entity<Guid>
{
    public const int MaxReferenceLength = 120;
    public const int MaxNotesLength = 1000;
    public const decimal MaxAmount = 10_000_000m;

    private VenuePayout()
    {
    }

    public Guid VenueId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Method { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public DateTime PaidAtUtc { get; private set; }
    public Guid RecordedByUserId { get; private set; }

    public static Result<VenuePayout> Record(
        Guid id,
        Guid venueId,
        decimal amount,
        string currency,
        string? method,
        string? reference,
        string? notes,
        DateTime paidAtUtc,
        Guid recordedByUserId,
        DateTime nowUtc)
    {
        var cleanMethod = method?.Trim() ?? string.Empty;
        var cleanReference = reference?.Trim() ?? string.Empty;
        var cleanNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
        {
            return Result.Failure<VenuePayout>(VenuePayoutErrors.InvalidAmount);
        }

        if (!PayoutMethods.All.Contains(cleanMethod))
        {
            return Result.Failure<VenuePayout>(VenuePayoutErrors.InvalidMethod);
        }

        if (cleanReference.Length is 0 or > MaxReferenceLength)
        {
            return Result.Failure<VenuePayout>(VenuePayoutErrors.ReferenceRequired);
        }

        if (cleanNotes?.Length > MaxNotesLength)
        {
            return Result.Failure<VenuePayout>(VenuePayoutErrors.NotesTooLong);
        }

        // A transfer is recorded after it happened, never ahead of time (a small allowance covers clock drift).
        if (paidAtUtc > nowUtc.AddMinutes(5) || paidAtUtc < nowUtc.AddYears(-2))
        {
            return Result.Failure<VenuePayout>(VenuePayoutErrors.InvalidPaidAt);
        }

        return Result.Success(new VenuePayout
        {
            Id = id,
            VenueId = venueId,
            Amount = amount,
            Currency = currency,
            Method = cleanMethod,
            Reference = cleanReference,
            Notes = cleanNotes,
            PaidAtUtc = paidAtUtc,
            RecordedByUserId = recordedByUserId,
            CreatedAtUtc = nowUtc,
        });
    }
}

public static class PayoutMethods
{
    public const string InstaPay = "InstaPay";
    public const string Wallet = "Wallet";
    public const string BankTransfer = "BankTransfer";
    public const string Cash = "Cash";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { InstaPay, Wallet, BankTransfer, Cash };
}

public static class VenuePayoutErrors
{
    public static readonly Error InvalidAmount = Error.Validation("Payout.InvalidAmount", "The payout amount must be a positive EGP amount with at most 2 decimals.");
    public static readonly Error ExceedsBalance = Error.Validation("Payout.ExceedsBalance", "The payout is more than the platform owes this venue.");
    public static readonly Error InvalidMethod = Error.Validation("Payout.InvalidMethod", "Unknown payout method.");
    public static readonly Error ReferenceRequired = Error.Validation("Payout.ReferenceRequired", "The transfer reference is required (at most 120 characters).");
    public static readonly Error NotesTooLong = Error.Validation("Payout.NotesTooLong", "Notes cannot exceed 1000 characters.");
    public static readonly Error InvalidPaidAt = Error.Validation("Payout.InvalidPaidAt", "The transfer date cannot be in the future or more than two years ago.");
    public static readonly Error VenueNotFound = Error.NotFound("Payout.VenueNotFound", "The venue was not found.");
}
