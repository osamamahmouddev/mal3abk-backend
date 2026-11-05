using Malabak.Modules.Teams.Domain;

namespace Malabak.Modules.Teams.Application.Common;

public static class TeamMapper
{
    public static TeamMemberDto ToMemberDto(TeamMember member)
    {
        return new TeamMemberDto(
            member.Id,
            member.PlayerId,
            member.Role,
            member.JerseyNumber,
            member.Position,
            member.JoinedAtUtc);
    }

    public static TeamSummaryDto ToSummaryDto(Team team, Guid? requestingPlayerId = null)
    {
        var myRole = requestingPlayerId.HasValue
            ? team.Members.FirstOrDefault(m => m.PlayerId == requestingPlayerId.Value && m.IsActive)?.Role
            : null;

        return new TeamSummaryDto(
            team.Id,
            team.Name,
            team.Slug,
            team.Description,
            team.LogoUrl,
            team.City,
            team.Governorate,
            team.CaptainId,
            team.Members.Count(m => m.IsActive),
            team.Rating,
            team.MatchesPlayed,
            team.IsRecruiting,
            team.IsOpenForChallenges,
            myRole);
    }

    public static TeamStatsDto ToStatsDto(Team team, int rank)
    {
        var winPct = team.MatchesPlayed > 0
            ? Math.Round((double)team.Wins / team.MatchesPlayed * 100.0, 1)
            : 0.0;

        return new TeamStatsDto(
            team.MatchesPlayed,
            team.Wins,
            team.Draws,
            team.Losses,
            team.GoalsFor,
            team.GoalsAgainst,
            team.GoalsFor - team.GoalsAgainst,
            winPct,
            team.Rating,
            rank);
    }

    public static TeamProfileDto ToProfileDto(
        Team team,
        int rank,
        Guid? requestingPlayerId,
        IReadOnlyList<MatchSummaryDto>? recentMatches = null,
        IReadOnlyList<MatchSummaryDto>? upcomingMatches = null)
    {
        var callerMember = requestingPlayerId.HasValue
            ? team.Members.FirstOrDefault(m => m.PlayerId == requestingPlayerId.Value && m.IsActive)
            : null;

        var isCallerMember = callerMember is not null;
        var canBeChallenged = team.IsOpenForChallenges && !isCallerMember;

        return new TeamProfileDto(
            team.Id,
            team.Name,
            team.Slug,
            team.Description,
            team.LogoUrl,
            team.CoverUrl,
            team.City,
            team.Governorate,
            team.District,
            team.CaptainId,
            team.IsPublic,
            team.IsRecruiting,
            team.IsOpenForChallenges,
            canBeChallenged,
            isCallerMember,
            callerMember?.Role,
            isCallerMember ? team.InviteCode : null,
            ToStatsDto(team, rank),
            team.Members.Where(m => m.IsActive).Select(ToMemberDto).ToList(),
            recentMatches ?? [],
            upcomingMatches ?? []);
    }

    public static TeamDetailDto ToDetailDto(Team team, bool includePrivateInfo = false)
    {
        return new TeamDetailDto(
            team.Id,
            team.Name,
            team.Slug,
            team.Description,
            team.LogoUrl,
            team.CoverUrl,
            team.City,
            team.Governorate,
            team.District,
            team.CaptainId,
            includePrivateInfo ? team.InviteCode : null,
            includePrivateInfo ? team.InviteCodeExpiresAtUtc : null,
            team.Rating,
            team.RatingDeviation,
            team.RatingVolatility,
            team.MatchesPlayed,
            team.Wins,
            team.Draws,
            team.Losses,
            team.GoalsFor,
            team.GoalsAgainst,
            team.IsRecruiting,
            team.IsOpenForChallenges,
            team.Members
                .Where(m => m.IsActive)
                .Select(ToMemberDto)
                .ToList());
    }
}
