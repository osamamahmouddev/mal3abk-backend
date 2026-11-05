using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Matches.Application.Matches.GetMatchAuditHistory;

public sealed class GetMatchAuditHistoryHandler : IQueryHandler<GetMatchAuditHistoryQuery, Result<IReadOnlyList<MatchResultAuditDto>>>
{
    private readonly IMatchesDbContext _dbContext;

    public GetMatchAuditHistoryHandler(IMatchesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<MatchResultAuditDto>>> HandleAsync(GetMatchAuditHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var matchExists = await _dbContext.Matches
            .AnyAsync(m => m.Id == query.MatchId, cancellationToken);

        if (!matchExists)
        {
            return Result.Failure<IReadOnlyList<MatchResultAuditDto>>(MatchErrors.NotFound);
        }

        var history = await _dbContext.MatchResultAuditRecords
            .Where(a => a.MatchId == query.MatchId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new MatchResultAuditDto(
                a.Id,
                a.MatchId,
                a.PreviousHomeScore,
                a.PreviousAwayScore,
                a.NewHomeScore,
                a.NewAwayScore,
                a.PreviousStatus,
                a.NewStatus,
                a.ChangedByUserId,
                a.ChangeType,
                a.Reason,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<MatchResultAuditDto>>(history);
    }
}
