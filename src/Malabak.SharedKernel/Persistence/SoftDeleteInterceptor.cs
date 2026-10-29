using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Malabak.SharedKernel.Persistence;

public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
{
    private readonly IClock _clock;

    public SoftDeleteInterceptor(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplySoftDelete(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplySoftDelete(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplySoftDelete(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var utcNow = _clock.UtcNow;
        var baseContext = context as BaseDbContext;

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State == EntityState.Deleted && baseContext?.IsMarkedForHardDelete(entry.Entity) != true)
            {
                entry.State = EntityState.Modified;
                entry.Entity.SoftDelete(utcNow);
            }
        }

        baseContext?.ClearHardDeletes();
    }
}
