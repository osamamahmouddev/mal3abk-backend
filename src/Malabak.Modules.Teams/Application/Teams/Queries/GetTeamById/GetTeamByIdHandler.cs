using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Queries.GetTeamById;

public sealed class GetTeamByIdHandler : IQueryHandler<GetTeamByIdQuery, Result<TeamDetailDto>>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IUserAccountsContract? _accounts;

    public GetTeamByIdHandler(ITeamsDbContext dbContext, IUserAccountsContract? accounts = null)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    public async Task<Result<TeamDetailDto>> HandleAsync(GetTeamByIdQuery query, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == query.TeamId, cancellationToken);

        if (team is null)
        {
            return Result.Failure<TeamDetailDto>(TeamErrors.NotFound);
        }

        var isCallerMember = query.RequestingPlayerId.HasValue &&
            team.Members.Any(m => m.PlayerId == query.RequestingPlayerId.Value && m.IsActive);

        var detail = TeamMapper.ToDetailDto(team, includePrivateInfo: isCallerMember);
        return Result.Success(detail with { Members = await detail.Members.WithNamesAsync(_accounts, cancellationToken) });
    }
}
