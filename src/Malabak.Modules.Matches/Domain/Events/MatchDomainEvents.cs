using Malabak.SharedKernel;

namespace Malabak.Modules.Matches.Domain.Events;

public sealed record MatchScheduledDomainEvent(Guid MatchId, Guid HomeTeamId, Guid AwayTeamId, DateTime ScheduledStartUtc) : IDomainEvent;
public sealed record MatchCancelledDomainEvent(Guid MatchId, Guid CancelledByTeamId, string? Reason) : IDomainEvent;
