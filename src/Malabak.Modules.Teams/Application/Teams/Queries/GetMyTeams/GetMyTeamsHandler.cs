using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetMyTeams;

public sealed class GetMyTeamsHandler : IQueryHandler<GetMyTeamsQuery, Result<IReadOnlyList<TeamSummaryDto>>>
{
    private readonly ITeamsDbContext _dbContext;

    public GetMyTeamsHandler(ITeamsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<TeamSummaryDto>>> HandleAsync(GetMyTeamsQuery query, CancellationToken cancellationToken = default)
    {
        var teams = await _dbContext.Teams
            .Include(t => t.Members)
            .AsNoTracking()
            .Where(t => t.Members.Any(m => m.PlayerId == query.PlayerId && m.IsActive))
            .OrderByDescending(t => t.Rating)
            .ToListAsync(cancellationToken);

        var dtos = teams.Select(TeamMapper.ToSummaryDto).ToList();

        return Result.Success<IReadOnlyList<TeamSummaryDto>>(dtos);
    }
}
