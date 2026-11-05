using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Domain.Events;

public sealed record MatchScheduledDomainEvent(Guid MatchId, Guid HomeTeamId, Guid AwayTeamId, DateTime ScheduledStartUtc) : IDomainEvent;
public sealed record MatchCancelledDomainEvent(Guid MatchId, Guid CancelledByTeamId, string? Reason) : IDomainEvent;
public sealed record MatchResultSubmittedDomainEvent(Guid MatchId, Guid SubmittedByTeamId, int HomeScore, int AwayScore) : IDomainEvent;
public sealed record MatchResultConfirmedDomainEvent(Guid MatchId, Guid HomeTeamId, Guid AwayTeamId, int HomeScore, int AwayScore, MatchOutcome Outcome) : IDomainEvent;
public sealed record MatchResultDisputedDomainEvent(Guid MatchId, Guid HomeTeamId, Guid AwayTeamId, Guid DisputingTeamId, string? Reason) : IDomainEvent;
