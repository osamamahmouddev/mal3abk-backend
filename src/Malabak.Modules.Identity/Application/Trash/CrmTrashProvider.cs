using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Application.Trash;

/// <summary>CRM contacts go to the trash like everything else; nothing financial points to them, so purging removes them outright.</summary>
public sealed class CrmTrashProvider : ITrashProvider
{
    private readonly IdentityDbContext _dbContext;
    private readonly IClock _clock;

    public CrmTrashProvider(IdentityDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public string Kind => "crm";

    public async Task<string?> GetNameAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.CrmContacts.AsNoTracking().Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<TrashedItem>> MoveToTrashAsync(Guid id, Guid adminId, CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.CrmContacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return Result.Failure<TrashedItem>(TrashErrors.NotFound);
        }

        contact.MoveToTrash(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(new TrashedItem(contact.Name, contact.VenueName));
    }

    public async Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.CrmContacts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted, cancellationToken);
        if (contact is null)
        {
            return Result.Failure(TrashErrors.CannotRestore);
        }

        contact.RestoreFromTrash(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PurgeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.CrmContacts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted, cancellationToken);
        if (contact is null)
        {
            return Result.Failure(TrashErrors.NotFound);
        }

        _dbContext.CrmContacts.Remove(contact);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
