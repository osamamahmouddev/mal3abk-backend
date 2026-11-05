using Malabak.Modules.Teams.Domain;

namespace Malabak.Modules.Teams.Application.Common;

public sealed record TeamMemberDto(
    Guid Id,
    Guid PlayerId,
    TeamRole Role,
    int? JerseyNumber,
    string? Position,
    DateTime JoinedAtUtc);

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
    bool IsOpenForChallenges);

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

public sealed record PagedList<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
