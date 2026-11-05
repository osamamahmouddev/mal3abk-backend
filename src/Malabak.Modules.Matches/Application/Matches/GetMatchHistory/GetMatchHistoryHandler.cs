using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Matches.GetMatchHistory;

public sealed class GetMatchHistoryHandler : IQueryHandler<GetMatchHistoryQuery, Result<PagedList<MatchDto>>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;
    private readonly IClock _clock;

    public GetMatchHistoryHandler(
        IMatchesDbContext dbContext,
        ITeamCaptaincyChecker captaincyChecker,
        IClock clock)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
        _clock = clock;
    }

    public async Task<Result<PagedList<MatchDto>>> HandleAsync(GetMatchHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var baseQuery = _dbContext.Matches
            .AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed
                     || m.Status == MatchStatus.Disputed
                     || m.Status == MatchStatus.Cancelled
                     || m.ScheduledEndUtc < _clock.UtcNow);

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(m => m.Status == query.Status.Value);
        }

        if (query.TeamId.HasValue && query.TeamId.Value != Guid.Empty)
        {
            var tid = query.TeamId.Value;
            baseQuery = baseQuery.Where(m => m.HomeTeamId == tid || m.AwayTeamId == tid);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var matches = await baseQuery
            .OrderByDescending(m => m.ScheduledStartUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtoList = new List<MatchDto>(matches.Count);
        foreach (var match in matches)
        {
            var homeTeamName = await _captaincyChecker.GetTeamNameAsync(match.HomeTeamId, cancellationToken) ?? "Home Team";
            var awayTeamName = await _captaincyChecker.GetTeamNameAsync(match.AwayTeamId, cancellationToken) ?? "Away Team";
            dtoList.Add(MatchMapper.ToDto(match, homeTeamName, awayTeamName));
        }

        return Result.Success(new PagedList<MatchDto>(dtoList, totalCount, page, pageSize));
    }
}
