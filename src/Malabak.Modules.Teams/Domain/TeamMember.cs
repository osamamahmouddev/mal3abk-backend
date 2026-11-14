using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Domain;

public sealed class TeamMember : Entity<Guid>
{
    public Guid TeamId { get; private set; }
    public Guid PlayerId { get; private set; }
    public TeamRole Role { get; private set; }
    public int? JerseyNumber { get; private set; }
    public string? Position { get; private set; }
    public DateTime JoinedAtUtc { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// Set when a captain or vice-captain removed this player. The team's shared invite code then no longer lets them
    /// back in; a leader lifts it by inviting them again or approving a join request from them.
    /// </summary>
    public bool RejoinBlocked { get; private set; }

    private TeamMember()
    {
    }

    public static TeamMember Create(
        Guid teamId,
        Guid playerId,
        TeamRole role,
        DateTime joinedAtUtc,
        int? jerseyNumber = null,
        string? position = null)
    {
        return new TeamMember
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            PlayerId = playerId,
            Role = role,
            JerseyNumber = jerseyNumber,
            Position = position,
            JoinedAtUtc = joinedAtUtc,
            IsActive = true,
            CreatedAtUtc = joinedAtUtc
        };
    }

    internal void UpdateRole(TeamRole newRole, DateTime updatedAtUtc)
    {
        Role = newRole;
        SetUpdatedAt(updatedAtUtc);
    }

    internal void Deactivate(DateTime deletedAtUtc, bool blockRejoin = false)
    {
        IsActive = false;
        RejoinBlocked = blockRejoin;
        SoftDelete(deletedAtUtc);
    }

    /// <summary>A leader invited the player again, so the shared code works for them once more.</summary>
    internal void AllowRejoin(DateTime updatedAtUtc)
    {
        if (!RejoinBlocked)
        {
            return;
        }

        RejoinBlocked = false;
        SetUpdatedAt(updatedAtUtc);
    }

    internal void Reactivate(TeamRole role, DateTime joinedAtUtc)
    {
        IsActive = true;
        RejoinBlocked = false;
        Role = role;
        JoinedAtUtc = joinedAtUtc;
        Restore();
        SetUpdatedAt(joinedAtUtc);
    }

    internal void UpdateProfile(int? jerseyNumber, string? position, DateTime updatedAtUtc)
    {
        JerseyNumber = jerseyNumber;
        Position = position;
        SetUpdatedAt(updatedAtUtc);
    }
}
