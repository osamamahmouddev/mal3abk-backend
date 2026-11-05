using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Domain.Events;

public sealed record TeamCreatedDomainEvent(
    Guid TeamId,
    string Name,
    string Slug,
    Guid CaptainId) : DomainEvent;

public sealed record TeamMemberJoinedDomainEvent(
    Guid TeamId,
    Guid PlayerId,
    TeamRole Role) : DomainEvent;

public sealed record TeamMemberRemovedDomainEvent(
    Guid TeamId,
    Guid PlayerId,
    Guid RemovedBy) : DomainEvent;

public sealed record CaptaincyTransferredDomainEvent(
    Guid TeamId,
    Guid PreviousCaptainId,
    Guid NewCaptainId) : DomainEvent;

public sealed record InviteCodeRotatedDomainEvent(
    Guid TeamId,
    string NewInviteCode,
    DateTime? ExpiresAtUtc) : DomainEvent;

public sealed record JoinRequestCreatedDomainEvent(
    Guid RequestId,
    Guid TeamId,
    Guid PlayerId) : DomainEvent;

public sealed record JoinRequestApprovedDomainEvent(
    Guid RequestId,
    Guid TeamId,
    Guid PlayerId,
    Guid ApprovedBy) : DomainEvent;

public sealed record JoinRequestDeclinedDomainEvent(
    Guid RequestId,
    Guid TeamId,
    Guid PlayerId,
    Guid DeclinedBy) : DomainEvent;
