using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Domain.Events;

public sealed record BookingHoldCreatedDomainEvent(
    Guid BookingId,
    Guid VenueId,
    Guid PitchId,
    Guid PlayerId,
    string Reference,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime HoldExpiresAtUtc) : DomainEvent;

public sealed record BookingProofSubmittedDomainEvent(
    Guid BookingId,
    Guid VenueId,
    Guid PitchId,
    Guid PlayerId,
    string Reference,
    Guid ProofId,
    DateTime SubmittedAtUtc) : DomainEvent;

public sealed record BookingUnderReviewDomainEvent(
    Guid BookingId,
    Guid VenueId,
    string Reference,
    string ReviewedByUserId,
    DateTime ReviewedAtUtc) : DomainEvent;

public sealed record BookingConfirmedDomainEvent(
    Guid BookingId,
    Guid VenueId,
    Guid PitchId,
    Guid PlayerId,
    string Reference,
    DateTime StartUtc,
    DateTime EndUtc,
    string ConfirmedByUserId,
    DateTime ConfirmedAtUtc) : DomainEvent;

public sealed record BookingRejectedDomainEvent(
    Guid BookingId,
    Guid VenueId,
    Guid PitchId,
    Guid PlayerId,
    string Reference,
    string Reason,
    bool CanResubmit,
    DateTime RejectedAtUtc) : DomainEvent;

public sealed record BookingExpiredDomainEvent(
    Guid BookingId,
    Guid VenueId,
    Guid PitchId,
    Guid PlayerId,
    string Reference,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime ExpiredAtUtc) : DomainEvent;

public sealed record BookingCancelledDomainEvent(
    Guid BookingId,
    Guid VenueId,
    Guid PitchId,
    Guid PlayerId,
    string Reference,
    DateTime StartUtc,
    DateTime EndUtc,
    string CancelledByUserId,
    string Reason,
    DateTime CancelledAtUtc) : DomainEvent;

public sealed record BookingCompletedDomainEvent(
    Guid BookingId,
    Guid VenueId,
    Guid PitchId,
    Guid PlayerId,
    string Reference,
    DateTime CompletedAtUtc) : DomainEvent;

public sealed record BookingNoShowDomainEvent(
    Guid BookingId,
    Guid VenueId,
    Guid PitchId,
    Guid PlayerId,
    string Reference,
    string Reason,
    string MarkedByUserId,
    DateTime MarkedAtUtc) : DomainEvent;
