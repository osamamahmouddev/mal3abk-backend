using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Domain;

public sealed class TeamJoinRequest : Entity<Guid>
{
    public Guid TeamId { get; private set; }
    public Guid PlayerId { get; private set; }
    public string? Message { get; private set; }
    public JoinRequestStatus Status { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByPlayerId { get; private set; }

    private TeamJoinRequest()
    {
    }

    public static TeamJoinRequest Create(
        Guid teamId,
        Guid playerId,
        string? message,
        DateTime createdAtUtc)
    {
        return new TeamJoinRequest
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            PlayerId = playerId,
            Message = message?.Trim(),
            Status = JoinRequestStatus.Pending,
            CreatedAtUtc = createdAtUtc
        };
    }

    internal void Approve(Guid reviewerPlayerId, DateTime reviewedAtUtc)
    {
        Status = JoinRequestStatus.Approved;
        ReviewedByPlayerId = reviewerPlayerId;
        ReviewedAtUtc = reviewedAtUtc;
        SetUpdatedAt(reviewedAtUtc);
    }

    internal void Decline(Guid reviewerPlayerId, DateTime reviewedAtUtc)
    {
        Status = JoinRequestStatus.Declined;
        ReviewedByPlayerId = reviewerPlayerId;
        ReviewedAtUtc = reviewedAtUtc;
        SetUpdatedAt(reviewedAtUtc);
    }

    internal void Cancel(DateTime cancelledAtUtc)
    {
        Status = JoinRequestStatus.Cancelled;
        SetUpdatedAt(cancelledAtUtc);
        SoftDelete(cancelledAtUtc);
    }
}
