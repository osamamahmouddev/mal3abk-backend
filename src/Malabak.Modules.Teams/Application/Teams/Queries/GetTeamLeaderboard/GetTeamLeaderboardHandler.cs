using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetTeamLeaderboard;

public sealed class GetTeamLeaderboardHandler : IQueryHandler<GetTeamLeaderboardQuery, Result<PagedList<TeamLeaderboardEntryDto>>>
{
    private static readonly TimeSpan _defaultCacheTtl = TimeSpan.FromMinutes(5);

    private readonly ITeamsDbContext _dbContext;
    private readonly ILeaderboardCache _cache;

    public GetTeamLeaderboardHandler(ITeamsDbContext dbContext, ILeaderboardCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<Result<PagedList<TeamLeaderboardEntryDto>>> HandleAsync(GetTeamLeaderboardQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 50);

        var cacheKey = $"leaderboard:{query.City?.Trim().ToLowerInvariant() ?? "all"}:{query.Governorate?.Trim().ToLowerInvariant() ?? "all"}:{page}:{pageSize}";
        var cached = await _cache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var queryable = _dbContext.Teams
            .Where(t => t.IsPublic && !t.IsDeleted && t.SuspendedAtUtc == null);

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            var city = query.City.Trim().ToLowerInvariant();
            queryable = queryable.Where(t => t.City.ToLower() == city);
        }

        if (!string.IsNullOrWhiteSpace(query.Governorate))
        {
            var gov = query.Governorate.Trim().ToLowerInvariant();
            queryable = queryable.Where(t => t.Governorate.ToLower() == gov);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var skip = (page - 1) * pageSize;

        var teams = await queryable
            .OrderByDescending(t => t.Rating)
            .ThenByDescending(t => (t.Wins * 3) + t.Draws)
            .ThenByDescending(t => t.GoalsFor - t.GoalsAgainst)
            .ThenByDescending(t => t.GoalsFor)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<TeamLeaderboardEntryDto>(teams.Count);
        for (var i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            var winPct = team.MatchesPlayed > 0
                ? Math.Round((double)team.Wins / team.MatchesPlayed * 100.0, 1)
                : 0.0;

            items.Add(new TeamLeaderboardEntryDto(
                Rank: skip + i + 1,
                TeamId: team.Id,
                Name: team.Name,
                Slug: team.Slug,
                LogoUrl: team.LogoUrl,
                City: team.City,
                Governorate: team.Governorate,
                Rating: team.Rating,
                RatingDeviation: team.RatingDeviation,
                MatchesPlayed: team.MatchesPlayed,
                Wins: team.Wins,
                Draws: team.Draws,
                Losses: team.Losses,
                Points: team.Points,
                GoalsFor: team.GoalsFor,
                GoalsAgainst: team.GoalsAgainst,
                GoalDifference: team.GoalDifference,
                WinPercentage: winPct));
        }

        var pagedList = new PagedList<TeamLeaderboardEntryDto>(items, totalCount, page, pageSize);
        await _cache.SetAsync(cacheKey, pagedList, _defaultCacheTtl, cancellationToken);

        return Result.Success(pagedList);
    }
}
