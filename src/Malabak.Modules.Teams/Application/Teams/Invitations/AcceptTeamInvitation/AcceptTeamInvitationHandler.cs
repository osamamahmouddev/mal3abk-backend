using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;

public sealed class AcceptTeamInvitationHandler : ICommandHandler<AcceptTeamInvitationCommand, Result<TeamDetailDto>>
{
    private readonly ITeamsDbContext _dbContext;
    private readonly IUserAccountsContract? _accounts;
    private readonly IClock _clock;

    public AcceptTeamInvitationHandler(ITeamsDbContext dbContext, IClock clock, IUserAccountsContract? accounts = null)
    {
        _accounts = accounts;
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<TeamDetailDto>> HandleAsync(AcceptTeamInvitationCommand command, CancellationToken cancellationToken = default)
    {
        var normalizedCode = command.InviteCode.Trim().ToUpperInvariant();

        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.InviteCode == normalizedCode, cancellationToken);

        if (team is null)
        {
            return Result.Failure<TeamDetailDto>(TeamErrors.InvalidInviteCode);
        }

        var activeTeamsCount = await _dbContext.TeamMembers
            .CountAsync(m => m.PlayerId == command.PlayerId && m.IsActive, cancellationToken);
        if (activeTeamsCount >= Team.MaxTeamsPerPlayer && !team.Members.Any(m => m.PlayerId == command.PlayerId && m.IsActive))
        {
            return Result.Failure<TeamDetailDto>(TeamErrors.MaxPlayerTeamsExceeded);
        }

        var joinResult = team.JoinWithInviteCode(normalizedCode, command.PlayerId, _clock.UtcNow);
        if (!joinResult.IsSuccess)
        {
            return Result.Failure<TeamDetailDto>(joinResult.Error);
        }

        var member = joinResult.Value;
        if (_dbContext.TeamMembers.Entry(member).State == EntityState.Detached)
        {
            _dbContext.TeamMembers.Add(member);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Player is now a member, so include private info (invite code)
        var detail = TeamMapper.ToDetailDto(team, includePrivateInfo: true);
        return Result.Success(detail with { Members = await detail.Members.WithNamesAsync(_accounts, cancellationToken) });
    }
}
