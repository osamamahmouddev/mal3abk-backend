using Malabak.Modules.Teams.Domain;

namespace Malabak.Modules.Teams.Application.Common;

public sealed record TeamMemberDto(
    Guid Id,
    Guid PlayerId,
    TeamRole Role,
    int? JerseyNumber,
    string? Position,
    DateTime JoinedAtUtc,
    string? PlayerName = null);

public sealed record TeamSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string City,
    string Governorate,
    Guid CaptainId,
    int MemberCount,
    double Rating,
    int MatchesPlayed,
    bool IsRecruiting,
    bool IsOpenForChallenges,
    TeamRole? MyRole = null);

public sealed record TeamDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string? CoverUrl,
    string City,
    string Governorate,
    string? District,
    Guid CaptainId,
    string? InviteCode,
    DateTime? InviteCodeExpiresAtUtc,
    double Rating,
    double RatingDeviation,
    double RatingVolatility,
    int MatchesPlayed,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst,
    bool IsRecruiting,
    bool IsOpenForChallenges,
    IReadOnlyList<TeamMemberDto> Members);

public sealed record TeamInvitationDto(
    Guid TeamId,
    string InviteCode,
    DateTime? ExpiresAtUtc);

public sealed record TeamDiscoveryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string City,
    string Governorate,
    string? District,
    double Rating,
    int MemberCount,
    int MatchesPlayed,
    bool IsRecruiting,
    bool IsOpenForChallenges,
    bool CanBeChallenged,
    DateTime? LastActivityAtUtc);

public sealed record TeamStatsDto(
    int MatchesPlayed,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    double WinPercentage,
    double Rating,
    int Rank);

public sealed record MatchSummaryDto(
    Guid MatchId,
    Guid HomeTeamId,
    string HomeTeamName,
    Guid AwayTeamId,
    string AwayTeamName,
    int? HomeScore,
    int? AwayScore,
    string Status,
    DateTime ScheduledAtUtc);

public sealed record TeamProfileDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string? CoverUrl,
    string City,
    string Governorate,
    string? District,
    Guid CaptainId,
    bool IsPublic,
    bool IsRecruiting,
    bool IsOpenForChallenges,
    bool CanBeChallenged,
    bool IsCallerMember,
    TeamRole? CallerRole,
    string? InviteCode,
    TeamStatsDto Stats,
    IReadOnlyList<TeamMemberDto> Roster,
    IReadOnlyList<MatchSummaryDto> RecentMatches,
    IReadOnlyList<MatchSummaryDto> UpcomingMatches);

public sealed record PagedList<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record TeamJoinRequestDto(
    Guid Id,
    Guid TeamId,
    Guid PlayerId,
    string? Message,
    JoinRequestStatus Status,
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByPlayerId,
    string? PlayerName = null);

public sealed record UpdateTeamSettingsRequest(
    bool IsPublic,
    bool IsRecruiting,
    bool IsOpenForChallenges);

public sealed record TeamLeaderboardEntryDto(
    int Rank,
    Guid TeamId,
    string Name,
    string Slug,
    string? LogoUrl,
    string City,
    string Governorate,
    double Rating,
    double RatingDeviation,
    int MatchesPlayed,
    int Wins,
    int Draws,
    int Losses,
    int Points,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    double WinPercentage);

public sealed record FairPlayReportDto(
    Guid Id,
    Guid ReporterTeamId,
    Guid ReporterPlayerId,
    Guid ReportedTeamId,
    string ReportedTeamName,
    Guid? MatchId,
    FairPlayReportCategory Category,
    string Description,
    string? EvidenceUrls,
    FairPlayReportStatus Status,
    FairPlayPenaltyType PenaltyType,
    int? PointsDeducted,
    string? AdminDecisionNotes,
    Guid? AdminReviewedByUserId,
    DateTime? AdminReviewedAtUtc,
    string? AppealReason,
    Guid? AppealedByPlayerId,
    DateTime? AppealedAtUtc,
    string? AdminAppealNotes,
    Guid? AdminAppealResolvedByUserId,
    DateTime? AdminAppealResolvedAtUtc,
    DateTime CreatedAtUtc);

public sealed record SubmitFairPlayReportRequest(
    Guid ReporterTeamId,
    FairPlayReportCategory Category,
    string Description,
    Guid? MatchId = null,
    string? EvidenceUrls = null);

public sealed record ReviewFairPlayReportRequest(
    bool IsApproved,
    FairPlayPenaltyType Penalty,
    int? PointsDeducted,
    string AdminNotes);

public sealed record AppealFairPlayReportRequest(
    string AppealReason);

public sealed record ResolveFairPlayAppealRequest(
    bool UpholdViolation,
    string AdminNotes);
