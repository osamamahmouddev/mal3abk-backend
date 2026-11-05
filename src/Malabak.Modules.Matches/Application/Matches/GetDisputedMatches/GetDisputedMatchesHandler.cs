using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Matches.GetDisputedMatches;

public sealed class GetDisputedMatchesHandler : IQueryHandler<GetDisputedMatchesQuery, Result<PagedList<DisputedMatchDto>>>
{
    private readonly IMatchesDbContext _dbContext;
    private readonly ITeamCaptaincyChecker _captaincyChecker;

    public GetDisputedMatchesHandler(IMatchesDbContext dbContext, ITeamCaptaincyChecker captaincyChecker)
    {
        _dbContext = dbContext;
        _captaincyChecker = captaincyChecker;
    }

    public async Task<Result<PagedList<DisputedMatchDto>>> HandleAsync(GetDisputedMatchesQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var baseQuery = _dbContext.Matches
            .AsNoTracking()
            .Where(m => m.Status == MatchStatus.Disputed || m.ResultStatus == ResultVerificationStatus.Disputed);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var matches = await baseQuery
            .OrderByDescending(m => m.DisputedAtUtc ?? m.ScheduledStartUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtoList = new List<DisputedMatchDto>(matches.Count);
        foreach (var match in matches)
        {
            var homeTeamName = await _captaincyChecker.GetTeamNameAsync(match.HomeTeamId, cancellationToken) ?? "Home Team";
            var awayTeamName = await _captaincyChecker.GetTeamNameAsync(match.AwayTeamId, cancellationToken) ?? "Away Team";
            var disputingTeamName = match.ResultVerifiedByTeamId.HasValue
                ? await _captaincyChecker.GetTeamNameAsync(match.ResultVerifiedByTeamId.Value, cancellationToken)
                : null;

            dtoList.Add(new DisputedMatchDto(
                match.Id,
                match.HomeTeamId,
                homeTeamName,
                match.AwayTeamId,
                awayTeamName,
                match.HomeScore,
                match.AwayScore,
                match.ResultVerifiedByTeamId,
                disputingTeamName,
                match.DisputeReason,
                match.DisputedAtUtc,
                match.ScheduledStartUtc));
        }

        return Result.Success(new PagedList<DisputedMatchDto>(dtoList, totalCount, page, pageSize));
    }
}
