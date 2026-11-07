using Malabak.Modules.Teams.Domain.Events;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Domain;

public sealed class Team : Entity<Guid>
{
    public const int MaxTeamMembers = 25;
    public const int MaxTeamsPerPlayer = 5;

    private readonly List<TeamMember> _members = [];
    private readonly List<TeamJoinRequest> _joinRequests = [];

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? CoverUrl { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string Governorate { get; private set; } = string.Empty;
    public string? District { get; private set; }
    public Guid CaptainId { get; private set; }
    public string InviteCode { get; private set; } = string.Empty;
    public DateTime? InviteCodeExpiresAtUtc { get; private set; }
    public bool IsRecruiting { get; private set; } = true;
    public bool IsOpenForChallenges { get; private set; } = true;
    public bool IsPublic { get; private set; } = true;

    // Pluggable Ranking & Glicko-2 defaults (ADR 0008)
    public double Rating { get; private set; } = 1500.0;
    public double RatingDeviation { get; private set; } = 350.0;
    public double RatingVolatility { get; private set; } = 0.06;
    public int MatchesPlayed { get; private set; }
    public int Wins { get; private set; }
    public int Draws { get; private set; }
    public int Losses { get; private set; }
    public int GoalsFor { get; private set; }
    public int GoalsAgainst { get; private set; }
    public int Points => (Wins * 3) + Draws;
    public int GoalDifference => GoalsFor - GoalsAgainst;

    public IReadOnlyCollection<TeamMember> Members => _members.AsReadOnly();
    public IReadOnlyCollection<TeamJoinRequest> JoinRequests => _joinRequests.AsReadOnly();

    private Team()
    {
    }

    public static Result<Team> Create(
        Guid id,
        string name,
        string slug,
        string city,
        string governorate,
        Guid captainId,
        string inviteCode,
        DateTime nowUtc,
        string? description = null,
        string? district = null,
        string? logoUrl = null,
        string? coverUrl = null,
        DateTime? inviteCodeExpiresAtUtc = null,
        bool isPublic = true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Team>(TeamErrors.InvalidName);
        }

        if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(governorate))
        {
            return Result.Failure<Team>(TeamErrors.InvalidLocation);
        }

        if (captainId == Guid.Empty)
        {
            return Result.Failure<Team>(TeamErrors.NotCaptain);
        }

        if (string.IsNullOrWhiteSpace(inviteCode) || inviteCode.Trim().Length != 6)
        {
            return Result.Failure<Team>(TeamErrors.InvalidInviteCode);
        }

        var team = new Team
        {
            Id = id,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            City = city.Trim(),
            Governorate = governorate.Trim(),
            District = district?.Trim(),
            LogoUrl = logoUrl?.Trim(),
            CoverUrl = coverUrl?.Trim(),
            CaptainId = captainId,
            InviteCode = inviteCode.Trim().ToUpperInvariant(),
            InviteCodeExpiresAtUtc = inviteCodeExpiresAtUtc,
            IsRecruiting = true,
            IsOpenForChallenges = true,
            IsPublic = isPublic,
            Rating = 1500.0,
            RatingDeviation = 350.0,
            RatingVolatility = 0.06,
            CreatedAtUtc = nowUtc
        };

        var captainMember = TeamMember.Create(team.Id, captainId, TeamRole.Captain, nowUtc);
        team._members.Add(captainMember);

        team.RaiseDomainEvent(new TeamCreatedDomainEvent(team.Id, team.Name, team.Slug, team.CaptainId));
        team.RaiseDomainEvent(new TeamMemberJoinedDomainEvent(team.Id, captainId, TeamRole.Captain));

        return Result.Success(team);
    }

    public Result<TeamMember> JoinWithInviteCode(string inviteCode, Guid playerId, DateTime nowUtc)
    {
        if (playerId == Guid.Empty)
        {
            return Result.Failure<TeamMember>(TeamErrors.PlayerNotInTeam);
        }

        if (IsSuspended)
        {
            return Result.Failure<TeamMember>(TeamErrors.Suspended);
        }

        if (!string.Equals(InviteCode, inviteCode?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<TeamMember>(TeamErrors.InvalidInviteCode);
        }

        if (InviteCodeExpiresAtUtc.HasValue && InviteCodeExpiresAtUtc.Value < nowUtc)
        {
            return Result.Failure<TeamMember>(TeamErrors.InviteCodeExpired);
        }

        var existingMember = _members.FirstOrDefault(m => m.PlayerId == playerId);
        if (existingMember is not null && existingMember.IsActive)
        {
            return Result.Failure<TeamMember>(TeamErrors.PlayerAlreadyInTeam);
        }

        if (_members.Count(m => m.IsActive) >= MaxTeamMembers)
        {
            return Result.Failure<TeamMember>(TeamErrors.TeamRosterFull);
        }

        if (existingMember is not null)
        {
            existingMember.Reactivate(TeamRole.Member, nowUtc);
            SetUpdatedAt(nowUtc);
            RaiseDomainEvent(new TeamMemberJoinedDomainEvent(Id, playerId, TeamRole.Member));
            return Result.Success(existingMember);
        }

        var newMember = TeamMember.Create(Id, playerId, TeamRole.Member, nowUtc);
        _members.Add(newMember);
        SetUpdatedAt(nowUtc);

        RaiseDomainEvent(new TeamMemberJoinedDomainEvent(Id, playerId, TeamRole.Member));

        return Result.Success(newMember);
    }

    public Result RotateInviteCode(string newInviteCode, Guid requestedByPlayerId, DateTime nowUtc, DateTime? expiresAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(newInviteCode) || newInviteCode.Trim().Length != 6)
        {
            return Result.Failure(TeamErrors.InvalidInviteCode);
        }

        var caller = _members.FirstOrDefault(m => m.PlayerId == requestedByPlayerId && m.IsActive);
        if (caller is null || (caller.Role != TeamRole.Captain && caller.Role != TeamRole.ViceCaptain))
        {
            return Result.Failure(TeamErrors.NotCaptainOrViceCaptain);
        }

        InviteCode = newInviteCode.Trim().ToUpperInvariant();
        InviteCodeExpiresAtUtc = expiresAtUtc;
        SetUpdatedAt(nowUtc);

        RaiseDomainEvent(new InviteCodeRotatedDomainEvent(Id, InviteCode, InviteCodeExpiresAtUtc));

        return Result.Success();
    }

    public Result RemoveMember(Guid memberPlayerId, Guid requestedByPlayerId, DateTime nowUtc)
    {
        var memberToRemove = _members.FirstOrDefault(m => m.PlayerId == memberPlayerId && m.IsActive);
        if (memberToRemove is null)
        {
            return Result.Failure(TeamErrors.MemberNotFound);
        }

        var caller = _members.FirstOrDefault(m => m.PlayerId == requestedByPlayerId && m.IsActive);
        if (caller is null)
        {
            return Result.Failure(TeamErrors.NotAuthorizedToManageRoster);
        }

        var isSelfRemoval = memberPlayerId == requestedByPlayerId;

        if (memberPlayerId == CaptainId)
        {
            return isSelfRemoval
                ? Result.Failure(TeamErrors.SelfRemovalMustTransferCaptaincy)
                : Result.Failure(TeamErrors.CannotRemoveCaptain);
        }

        var isCaptain = caller.Role == TeamRole.Captain;
        var isViceCaptain = caller.Role == TeamRole.ViceCaptain && memberToRemove.Role == TeamRole.Member;

        if (!isSelfRemoval && !isCaptain && !isViceCaptain)
        {
            return Result.Failure(TeamErrors.NotAuthorizedToManageRoster);
        }

        memberToRemove.Deactivate(nowUtc);
        SetUpdatedAt(nowUtc);

        RaiseDomainEvent(new TeamMemberRemovedDomainEvent(Id, memberPlayerId, requestedByPlayerId));

        return Result.Success();
    }

    public Result TransferCaptaincy(Guid newCaptainPlayerId, Guid currentCaptainPlayerId, DateTime nowUtc)
    {
        if (currentCaptainPlayerId != CaptainId)
        {
            return Result.Failure(TeamErrors.NotCaptain);
        }

        if (newCaptainPlayerId == currentCaptainPlayerId)
        {
            return Result.Failure(TeamErrors.CannotTransferToSelf);
        }

        var newCaptain = _members.FirstOrDefault(m => m.PlayerId == newCaptainPlayerId && m.IsActive);
        if (newCaptain is null)
        {
            return Result.Failure(TeamErrors.MemberNotFound);
        }

        var currentCaptain = _members.FirstOrDefault(m => m.PlayerId == currentCaptainPlayerId && m.IsActive);
        if (currentCaptain is not null)
        {
            currentCaptain.UpdateRole(TeamRole.Member, nowUtc);
        }

        newCaptain.UpdateRole(TeamRole.Captain, nowUtc);
        CaptainId = newCaptainPlayerId;
        SetUpdatedAt(nowUtc);

        RaiseDomainEvent(new CaptaincyTransferredDomainEvent(Id, currentCaptainPlayerId, newCaptainPlayerId));

        return Result.Success();
    }

    public Result UpdateMemberRole(Guid targetPlayerId, TeamRole newRole, Guid requestedByPlayerId, DateTime nowUtc)
    {
        if (requestedByPlayerId != CaptainId)
        {
            return Result.Failure(TeamErrors.NotCaptain);
        }

        if (targetPlayerId == CaptainId)
        {
            return Result.Failure(TeamErrors.CannotChangeCaptainRole);
        }

        if (newRole == TeamRole.Captain)
        {
            return TransferCaptaincy(targetPlayerId, requestedByPlayerId, nowUtc);
        }

        var member = _members.FirstOrDefault(m => m.PlayerId == targetPlayerId && m.IsActive);
        if (member is null)
        {
            return Result.Failure(TeamErrors.MemberNotFound);
        }

        member.UpdateRole(newRole, nowUtc);
        SetUpdatedAt(nowUtc);

        return Result.Success();
    }

    public Result UpdateProfile(
        string name,
        string? description,
        string city,
        string governorate,
        string? district,
        string? logoUrl,
        string? coverUrl,
        bool isRecruiting,
        bool isOpenForChallenges,
        Guid requestedByPlayerId,
        DateTime nowUtc,
        bool? isPublic = null)
    {
        var caller = _members.FirstOrDefault(m => m.PlayerId == requestedByPlayerId && m.IsActive);
        if (caller is null || (caller.Role != TeamRole.Captain && caller.Role != TeamRole.ViceCaptain))
        {
            return Result.Failure(TeamErrors.NotCaptainOrViceCaptain);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(TeamErrors.InvalidName);
        }

        if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(governorate))
        {
            return Result.Failure(TeamErrors.InvalidLocation);
        }

        Name = name.Trim();
        Description = description?.Trim();
        City = city.Trim();
        Governorate = governorate.Trim();
        District = district?.Trim();
        LogoUrl = logoUrl?.Trim();
        CoverUrl = coverUrl?.Trim();
        IsRecruiting = isRecruiting;
        IsOpenForChallenges = isOpenForChallenges;
        if (isPublic.HasValue)
        {
            IsPublic = isPublic.Value;
        }
        SetUpdatedAt(nowUtc);

        return Result.Success();
    }

    public Result SetPrivacy(bool isPublic, Guid requestedByPlayerId, DateTime nowUtc)
    {
        var caller = _members.FirstOrDefault(m => m.PlayerId == requestedByPlayerId && m.IsActive);
        if (caller is null || (caller.Role != TeamRole.Captain && caller.Role != TeamRole.ViceCaptain))
        {
            return Result.Failure(TeamErrors.NotCaptainOrViceCaptain);
        }

        IsPublic = isPublic;
        SetUpdatedAt(nowUtc);
        return Result.Success();
    }

    public Result UpdateSettings(
        bool isPublic,
        bool isRecruiting,
        bool isOpenForChallenges,
        Guid requestedByPlayerId,
        DateTime nowUtc)
    {
        var caller = _members.FirstOrDefault(m => m.PlayerId == requestedByPlayerId && m.IsActive);
        if (caller is null || (caller.Role != TeamRole.Captain && caller.Role != TeamRole.ViceCaptain))
        {
            return Result.Failure(TeamErrors.NotCaptainOrViceCaptain);
        }

        if (IsSuspended && (isRecruiting || isOpenForChallenges))
        {
            return Result.Failure(TeamErrors.Suspended);
        }

        IsPublic = isPublic;
        IsRecruiting = isRecruiting;
        IsOpenForChallenges = isOpenForChallenges;
        SetUpdatedAt(nowUtc);

        return Result.Success();
    }

    public Result LeaveTeam(Guid playerId, DateTime nowUtc)
    {
        return RemoveMember(playerId, playerId, nowUtc);
    }

    public Result<TeamJoinRequest> CreateJoinRequest(Guid playerId, string? message, int activeTeamsCount, DateTime nowUtc)
    {
        if (playerId == Guid.Empty)
        {
            return Result.Failure<TeamJoinRequest>(TeamErrors.PlayerNotInTeam);
        }

        if (IsSuspended)
        {
            return Result.Failure<TeamJoinRequest>(TeamErrors.Suspended);
        }

        if (!IsRecruiting)
        {
            return Result.Failure<TeamJoinRequest>(TeamErrors.TeamNotRecruiting);
        }

        if (_members.Count(m => m.IsActive) >= MaxTeamMembers)
        {
            return Result.Failure<TeamJoinRequest>(TeamErrors.TeamRosterFull);
        }

        if (activeTeamsCount >= MaxTeamsPerPlayer)
        {
            return Result.Failure<TeamJoinRequest>(TeamErrors.MaxPlayerTeamsExceeded);
        }

        if (_members.Any(m => m.PlayerId == playerId && m.IsActive))
        {
            return Result.Failure<TeamJoinRequest>(TeamErrors.PlayerAlreadyInTeam);
        }

        if (_joinRequests.Any(r => r.PlayerId == playerId && r.Status == JoinRequestStatus.Pending))
        {
            return Result.Failure<TeamJoinRequest>(TeamErrors.DuplicateJoinRequest);
        }

        var request = TeamJoinRequest.Create(Id, playerId, message, nowUtc);
        _joinRequests.Add(request);
        SetUpdatedAt(nowUtc);

        RaiseDomainEvent(new JoinRequestCreatedDomainEvent(request.Id, Id, playerId));

        return Result.Success(request);
    }

    public Result<TeamMember> ApproveJoinRequest(Guid requestId, Guid reviewerPlayerId, DateTime nowUtc)
    {
        var reviewer = _members.FirstOrDefault(m => m.PlayerId == reviewerPlayerId && m.IsActive);
        if (reviewer is null || (reviewer.Role != TeamRole.Captain && reviewer.Role != TeamRole.ViceCaptain))
        {
            return Result.Failure<TeamMember>(TeamErrors.NotCaptainOrViceCaptain);
        }

        var request = _joinRequests.FirstOrDefault(r => r.Id == requestId);
        if (request is null)
        {
            return Result.Failure<TeamMember>(TeamErrors.JoinRequestNotFound);
        }

        if (request.Status != JoinRequestStatus.Pending)
        {
            return Result.Failure<TeamMember>(TeamErrors.JoinRequestAlreadyHandled);
        }

        if (_members.Count(m => m.IsActive) >= MaxTeamMembers)
        {
            return Result.Failure<TeamMember>(TeamErrors.TeamRosterFull);
        }

        request.Approve(reviewerPlayerId, nowUtc);

        var existingMember = _members.FirstOrDefault(m => m.PlayerId == request.PlayerId);
        TeamMember member;
        if (existingMember is not null)
        {
            existingMember.Reactivate(TeamRole.Member, nowUtc);
            member = existingMember;
        }
        else
        {
            member = TeamMember.Create(Id, request.PlayerId, TeamRole.Member, nowUtc);
            _members.Add(member);
        }

        SetUpdatedAt(nowUtc);

        RaiseDomainEvent(new JoinRequestApprovedDomainEvent(request.Id, Id, request.PlayerId, reviewerPlayerId));
        RaiseDomainEvent(new TeamMemberJoinedDomainEvent(Id, request.PlayerId, TeamRole.Member));

        return Result.Success(member);
    }

    public Result DeclineJoinRequest(Guid requestId, Guid reviewerPlayerId, DateTime nowUtc)
    {
        var reviewer = _members.FirstOrDefault(m => m.PlayerId == reviewerPlayerId && m.IsActive);
        if (reviewer is null || (reviewer.Role != TeamRole.Captain && reviewer.Role != TeamRole.ViceCaptain))
        {
            return Result.Failure(TeamErrors.NotCaptainOrViceCaptain);
        }

        var request = _joinRequests.FirstOrDefault(r => r.Id == requestId);
        if (request is null)
        {
            return Result.Failure(TeamErrors.JoinRequestNotFound);
        }

        if (request.Status != JoinRequestStatus.Pending)
        {
            return Result.Failure(TeamErrors.JoinRequestAlreadyHandled);
        }

        request.Decline(reviewerPlayerId, nowUtc);
        SetUpdatedAt(nowUtc);

        RaiseDomainEvent(new JoinRequestDeclinedDomainEvent(request.Id, Id, request.PlayerId, reviewerPlayerId));

        return Result.Success();
    }

    public const int MaxSuspensionReasonLength = 500;

    /// <summary>Set while platform staff have suspended the team (TASK-705): no challenges, matches or new members.</summary>
    public DateTime? SuspendedAtUtc { get; private set; }
    public string? SuspensionReason { get; private set; }
    public bool IsSuspended => SuspendedAtUtc is not null;

    public Result Suspend(string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxSuspensionReasonLength)
        {
            return Result.Failure(Error.Validation("Team.SuspensionReasonRequired", "A suspension reason is required."));
        }

        if (IsSuspended)
        {
            return Result.Failure(Error.Conflict("Team.AlreadySuspended", "The team is already suspended."));
        }

        SuspendedAtUtc = nowUtc;
        SuspensionReason = reason.Trim();
        IsOpenForChallenges = false;
        IsRecruiting = false;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Reactivate(DateTime nowUtc)
    {
        if (!IsSuspended)
        {
            return Result.Failure(Error.Conflict("Team.NotSuspended", "The team is not suspended."));
        }

        SuspendedAtUtc = null;
        SuspensionReason = null;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public void UpdateRanking(double rating, double deviation, double volatility, DateTime nowUtc)
    {
        Rating = rating;
        RatingDeviation = deviation;
        RatingVolatility = volatility;
        SetUpdatedAt(nowUtc);
    }

    public void RecordMatchResult(bool isWin, bool isDraw, bool isLoss, int goalsFor, int goalsAgainst, DateTime nowUtc)
    {
        MatchesPlayed++;
        if (isWin) Wins++;
        if (isDraw) Draws++;
        if (isLoss) Losses++;
        GoalsFor += goalsFor;
        GoalsAgainst += goalsAgainst;
        SetUpdatedAt(nowUtc);
    }

    public void ResetStats(DateTime nowUtc)
    {
        Rating = 1500.0;
        RatingDeviation = 350.0;
        RatingVolatility = 0.06;
        MatchesPlayed = 0;
        Wins = 0;
        Draws = 0;
        Losses = 0;
        GoalsFor = 0;
        GoalsAgainst = 0;
        SetUpdatedAt(nowUtc);
    }

    public void ApplyInactivityDecay(double newRatingDeviation, DateTime nowUtc)
    {
        RatingDeviation = Math.Min(350.0, Math.Max(RatingDeviation, newRatingDeviation));
        SetUpdatedAt(nowUtc);
    }
}
