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
