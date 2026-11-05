using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Domain.Events;

public sealed record ChallengeSentDomainEvent(
    Guid ChallengeId,
    Guid ChallengerTeamId,
    Guid ChallengedTeamId,
    Guid ChallengerCaptainId,
    DateTime ProposedDateUtc,
    Guid? ProposedPitchId,
    Guid? ProposedVenueId) : DomainEvent;

public sealed record ChallengeCounterProposedDomainEvent(
    Guid ChallengeId,
    Guid ProposingTeamId,
    Guid ProposingCaptainId,
    DateTime NewProposedDateUtc,
    Guid? NewProposedPitchId,
    Guid? NewProposedVenueId) : DomainEvent;

public sealed record ChallengeAcceptedDomainEvent(
    Guid ChallengeId,
    Guid ChallengerTeamId,
    Guid ChallengedTeamId,
    Guid AcceptingCaptainId,
    DateTime ScheduledDateUtc,
    Guid? PitchId,
    Guid? VenueId) : DomainEvent;

public sealed record ChallengeDeclinedDomainEvent(
    Guid ChallengeId,
    Guid DecliningTeamId,
    Guid DecliningCaptainId,
    string? Reason) : DomainEvent;

public sealed record ChallengeCancelledDomainEvent(
    Guid ChallengeId,
    Guid CancellingTeamId,
    Guid CancellingCaptainId) : DomainEvent;
