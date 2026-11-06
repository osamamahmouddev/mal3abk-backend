using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Domain;

public enum DisputeOpenedBy
{
    Player = 0,
    Venue = 1,
    System = 2
}

public enum DisputeStatus
{
    Open = 0,
    UnderReview = 1,
    Resolved = 2
}

/// <summary>What the admin decided and what it does to the booking.</summary>
public enum DisputeOutcome
{
    /// <summary>The payment was valid: a booking still waiting on its proof is confirmed.</summary>
    ConfirmBooking = 0,

    /// <summary>The player is owed their deposit: an active booking is cancelled and the full deposit is recorded as owed.</summary>
    RefundPlayer = 1,

    /// <summary>The venue was right: a booking still waiting on its proof is rejected; anything else stays as it is.</summary>
    FavorVenue = 2,

    /// <summary>No change to the booking (duplicate, withdrawn, already settled).</summary>
    Dismiss = 3
}

/// <summary>
/// A disagreement about a booking's payment proof or deposit (TASK-704), opened by the player, the venue or the system, with
/// evidence from both sides and a final, reasoned admin decision. One open dispute per booking.
/// </summary>
public sealed class PaymentDispute : Entity<Guid>
{
    public const int MaxTextLength = 2000;

    private readonly List<DisputeEvidence> _evidence = [];

    private PaymentDispute()
    {
    }

    public Guid BookingId { get; private set; }
    public Guid VenueId { get; private set; }
    public Guid PlayerId { get; private set; }
    public DisputeOpenedBy OpenedBy { get; private set; }
    public Guid? OpenedByUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DisputeStatus Status { get; private set; }
    public Guid? ReviewerId { get; private set; }
    public DisputeOutcome? Outcome { get; private set; }
    public string? ResolutionNote { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public IReadOnlyCollection<DisputeEvidence> Evidence => _evidence.AsReadOnly();

    public static Result<PaymentDispute> Open(Booking booking, DisputeOpenedBy openedBy, Guid? openedByUserId, string? reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxTextLength || !Enum.IsDefined(openedBy))
        {
            return Result.Failure<PaymentDispute>(DisputeErrors.ReasonRequired);
        }

        if (booking.IsManualBooking || booking.Status is BookingStatus.Draft)
        {
            return Result.Failure<PaymentDispute>(DisputeErrors.NotDisputable);
        }

        return Result.Success(new PaymentDispute
        {
            Id = Guid.CreateVersion7(),
            BookingId = booking.Id,
            VenueId = booking.VenueId,
            PlayerId = booking.PlayerId,
            OpenedBy = openedBy,
            OpenedByUserId = openedByUserId,
            Reason = reason.Trim(),
            Status = DisputeStatus.Open,
            CreatedAtUtc = nowUtc
        });
    }

    public Result<DisputeEvidence> AddEvidence(DisputeOpenedBy side, Guid? authorId, string? note, Guid? proofId, DateTime nowUtc)
    {
        if (Status == DisputeStatus.Resolved)
        {
            return Result.Failure<DisputeEvidence>(DisputeErrors.AlreadyResolved);
        }

        if ((string.IsNullOrWhiteSpace(note) && proofId is null) || note?.Trim().Length > MaxTextLength)
        {
            return Result.Failure<DisputeEvidence>(DisputeErrors.InvalidEvidence);
        }

        var evidence = new DisputeEvidence(Guid.CreateVersion7(), Id, side, authorId, note?.Trim(), proofId, nowUtc);
        _evidence.Add(evidence);
        UpdatedAtUtc = nowUtc;
        return Result.Success(evidence);
    }

    public Result StartReview(Guid reviewerId, DateTime nowUtc)
    {
        if (Status == DisputeStatus.Resolved)
        {
            return Result.Failure(DisputeErrors.AlreadyResolved);
        }

        ReviewerId = reviewerId;
        Status = DisputeStatus.UnderReview;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Resolve(DisputeOutcome outcome, string? note, Guid reviewerId, DateTime nowUtc)
    {
        if (Status == DisputeStatus.Resolved)
        {
            return Result.Failure(DisputeErrors.AlreadyResolved);
        }

        if (!Enum.IsDefined(outcome) || string.IsNullOrWhiteSpace(note) || note.Trim().Length > MaxTextLength)
        {
            return Result.Failure(DisputeErrors.ReasonRequired);
        }

        Outcome = outcome;
        ResolutionNote = note.Trim();
        ReviewerId = reviewerId;
        Status = DisputeStatus.Resolved;
        ResolvedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}

public sealed class DisputeEvidence
{
    private DisputeEvidence()
    {
    }

    internal DisputeEvidence(Guid id, Guid disputeId, DisputeOpenedBy side, Guid? authorId, string? note, Guid? proofId, DateTime createdAtUtc)
    {
        Id = id;
        DisputeId = disputeId;
        Side = side;
        AuthorId = authorId;
        Note = note;
        ProofId = proofId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid DisputeId { get; private set; }
    public DisputeOpenedBy Side { get; private set; }
    public Guid? AuthorId { get; private set; }
    public string? Note { get; private set; }

    /// <summary>A payment proof of the same booking (the file stays in the private proof store).</summary>
    public Guid? ProofId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}

public static class DisputeErrors
{
    public static readonly Error ReasonRequired = Error.Validation("Dispute.ReasonRequired", "Explain the dispute (up to 2000 characters).");
    public static readonly Error InvalidEvidence = Error.Validation("Dispute.InvalidEvidence", "Add a note or choose a payment proof of this booking.");
    public static readonly Error NotDisputable = Error.Conflict("Dispute.NotDisputable", "This booking has no payment that can be disputed.");
    public static readonly Error AlreadyOpen = Error.Conflict("Dispute.AlreadyOpen", "A dispute is already open for this booking.");
    public static readonly Error AlreadyResolved = Error.Conflict("Dispute.AlreadyResolved", "The dispute is already resolved.");
    public static readonly Error NotFound = Error.NotFound("Dispute.NotFound", "Dispute not found.");
    public static readonly Error OutcomeNotApplicable = Error.Conflict("Dispute.OutcomeNotApplicable",
        "That decision does not fit the booking's current state (for example, only a booking still waiting on its proof can be confirmed).");
    public static readonly Error InvalidFilter = Error.Validation("Dispute.InvalidFilter", "Invalid dispute filter.");
}
