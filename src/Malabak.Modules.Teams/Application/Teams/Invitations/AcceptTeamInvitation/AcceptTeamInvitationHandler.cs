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
        var normalizedCode = TeamInviteCodes.Normalize(command.InviteCode);
        if (normalizedCode is null)
        {
            return Result.Failure<TeamDetailDto>(TeamErrors.InvalidInviteCode);
        }

        var teamId = await _dbContext.Teams
            .Where(t => t.InviteCode == normalizedCode)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (teamId is null)
        {
            return Result.Failure<TeamDetailDto>(TeamErrors.InvalidInviteCode);
        }

        await using var transaction = await TeamConcurrency.BeginAsync(_dbContext, cancellationToken);
        await TeamConcurrency.LockPlayerAsync(_dbContext, command.PlayerId, cancellationToken);
        await TeamConcurrency.LockTeamAsync(_dbContext, teamId.Value, cancellationToken);

        // Re-read under the locks: the roster, the code and the player's other teams may have changed meanwhile.
        var team = await _dbContext.Teams
            .Include(t => t.Members)
            .Include(t => t.JoinRequests)
            .FirstOrDefaultAsync(t => t.Id == teamId.Value, cancellationToken);
        if (team is null)
        {
            return Result.Failure<TeamDetailDto>(TeamErrors.InvalidInviteCode);
        }

        var activeTeamsCount = await _dbContext.TeamMembers
            .CountAsync(m => m.PlayerId == command.PlayerId && m.IsActive, cancellationToken);

        // Ended memberships are soft-deleted, so the removal flag is read past the filter.
        var rejoinBlocked = await _dbContext.TeamMembers
            .IgnoreQueryFilters()
            .AnyAsync(m => m.TeamId == team.Id && m.PlayerId == command.PlayerId && !m.IsActive && m.RejoinBlocked, cancellationToken);

        var joinResult = team.JoinWithInviteCode(normalizedCode, command.PlayerId, _clock.UtcNow, activeTeamsCount, rejoinBlocked);
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
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        // Player is now a member, so include private info (invite code)
        var detail = TeamMapper.ToDetailDto(team, includePrivateInfo: true);
        return Result.Success(detail with { Members = await detail.Members.WithNamesAsync(_accounts, cancellationToken) });
    }
}
