using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Queries.DiscoverTeams;

public sealed class DiscoverTeamsHandler : IQueryHandler<DiscoverTeamsQuery, Result<PagedList<TeamDiscoveryDto>>>
{
    private readonly ITeamsDbContext _dbContext;

    public DiscoverTeamsHandler(ITeamsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedList<TeamDiscoveryDto>>> HandleAsync(DiscoverTeamsQuery query, CancellationToken cancellationToken = default)
    {
        // 1. Base query: Protect private teams (Privacy rule)
        var queryable = _dbContext.Teams
            .Include(t => t.Members)
            .AsNoTracking()
            .Where(t => t.IsPublic);

        // 2. Filters
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            queryable = queryable.Where(t =>
                t.Name.ToLower().Contains(search) ||
                t.Slug.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            var city = query.City.Trim().ToLowerInvariant();
            queryable = queryable.Where(t => t.City.ToLower() == city);
        }

        if (!string.IsNullOrWhiteSpace(query.Governorate))
        {
            var governorate = query.Governorate.Trim().ToLowerInvariant();
            queryable = queryable.Where(t => t.Governorate.ToLower() == governorate);
        }

        if (!string.IsNullOrWhiteSpace(query.District))
        {
            var district = query.District.Trim().ToLowerInvariant();
            queryable = queryable.Where(t => t.District != null && t.District.ToLower() == district);
        }

        if (query.MinRating.HasValue)
        {
            queryable = queryable.Where(t => t.Rating >= query.MinRating.Value);
        }

        if (query.MaxRating.HasValue)
        {
            queryable = queryable.Where(t => t.Rating <= query.MaxRating.Value);
        }

        if (query.IsRecruiting.HasValue)
        {
            queryable = queryable.Where(t => t.IsRecruiting == query.IsRecruiting.Value);
        }

        if (query.IsOpenForChallenges.HasValue)
        {
            queryable = queryable.Where(t => t.IsOpenForChallenges == query.IsOpenForChallenges.Value);
        }

        // 3. Sorting
        queryable = query.SortBy?.ToLowerInvariant() switch
        {
            "rating_asc" => queryable.OrderBy(t => t.Rating),
            "name" => queryable.OrderBy(t => t.Name),
            "activity" => queryable.OrderByDescending(t => t.UpdatedAtUtc ?? t.CreatedAtUtc),
            _ => queryable.OrderByDescending(t => t.Rating)
        };

        // 4. Count and Pagination
        var totalCount = await queryable.CountAsync(cancellationToken);

        var teams = await queryable
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        // 5. Map to DTOs including Challenge Eligibility Flag
        var items = teams.Select(team =>
        {
            var isMemberOfTeam = query.RequestingPlayerId.HasValue &&
                team.Members.Any(m => m.PlayerId == query.RequestingPlayerId.Value && m.IsActive);

            var canBeChallenged = team.IsOpenForChallenges && !isMemberOfTeam;

            return new TeamDiscoveryDto(
                team.Id,
                team.Name,
                team.Slug,
                team.Description,
                team.LogoUrl,
                team.City,
                team.Governorate,
                team.District,
                team.Rating,
                team.Members.Count(m => m.IsActive),
                team.MatchesPlayed,
                team.IsRecruiting,
                team.IsOpenForChallenges,
                canBeChallenged,
                team.UpdatedAtUtc ?? team.CreatedAtUtc);
        }).ToList();

        return Result.Success(new PagedList<TeamDiscoveryDto>(items, totalCount, query.Page, query.PageSize));
    }
}
