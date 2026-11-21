using Malabak.Modules.Teams.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Teams.Application.Trash;

/// <summary>Deleting a team from the admin console hides it everywhere and keeps it, members included, restorable.</summary>
public sealed class TeamTrashProvider : ITrashProvider
{
    private readonly TeamsDbContext _dbContext;
    private readonly IClock _clock;

    public TeamTrashProvider(TeamsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public string Kind => "team";

    public async Task<string?> GetNameAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Teams.AsNoTracking().Where(t => t.Id == id).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<TrashedItem>> MoveToTrashAsync(Guid id, Guid adminId, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (team is null)
        {
            return Result.Failure<TrashedItem>(TrashErrors.NotFound);
        }

        team.MoveToTrash(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(new TrashedItem(team.Name, string.IsNullOrWhiteSpace(team.City) ? null : team.City));
    }

    public async Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id && t.IsDeleted, cancellationToken);
        if (team is null)
        {
            return Result.Failure(TrashErrors.CannotRestore);
        }

        team.RestoreFromTrash(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PurgeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var team = await _dbContext.Teams.IgnoreQueryFilters()
            .Include(t => t.Members)
            .Include(t => t.JoinRequests)
            .FirstOrDefaultAsync(t => t.Id == id && t.IsDeleted, cancellationToken);
        if (team is null)
        {
            return Result.Failure(TrashErrors.NotFound);
        }

        team.Purge(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
