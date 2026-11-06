using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Domain;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Infrastructure.Services;

public sealed class TeamCaptaincyChecker : ITeamCaptaincyChecker
{
    private readonly ITeamsDbContext _dbContext;

    public TeamCaptaincyChecker(ITeamsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> IsCaptainOrViceCaptainAsync(Guid teamId, Guid playerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TeamMembers
            .AsNoTracking()
            .AnyAsync(m => m.TeamId == teamId
                        && m.PlayerId == playerId
                        && m.IsActive
                        && (m.Role == TeamRole.Captain || m.Role == TeamRole.ViceCaptain)
                        // A suspended team (TASK-705) cannot act through its captains: no challenges, results or bookings.
                        && _dbContext.Teams.Any(t => t.Id == teamId && t.SuspendedAtUtc == null),
                cancellationToken);
    }

    public async Task<Guid?> GetCaptainPlayerIdAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Teams
            .AsNoTracking()
            .Where(t => t.Id == teamId)
            .Select(t => (Guid?)t.CaptainId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetTeamNameAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Teams
            .AsNoTracking()
            .Where(t => t.Id == teamId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> CanBeChallengedAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Teams
            .AsNoTracking()
            .Where(t => t.Id == teamId)
            .Select(t => t.IsPublic && t.IsOpenForChallenges && t.SuspendedAtUtc == null)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
