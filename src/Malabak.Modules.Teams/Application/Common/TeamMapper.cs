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

    public static TeamSummaryDto ToSummaryDto(Team team)
    {
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
            team.IsOpenForChallenges);
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
