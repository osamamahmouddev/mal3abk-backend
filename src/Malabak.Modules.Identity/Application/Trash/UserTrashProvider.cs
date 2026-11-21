using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Privacy;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Application.Trash;

/// <summary>
/// Deleting a user from the admin console follows the account-deletion rules (ADR-0007): the account is closed and signed out
/// everywhere at once; restoring reopens it; purging erases its personal data in every module while bookings and payments
/// keep their now-anonymous owner.
/// </summary>
public sealed class UserTrashProvider : ITrashProvider
{
    private static readonly string[] _staffRoles = [UserRole.Admin, UserRole.SuperAdmin, UserRole.SupportAgent, UserRole.Finance];

    private readonly IdentityDbContext _dbContext;
    private readonly PrivacyHandlers _privacy;
    private readonly AccountAnonymizer _anonymizer;
    private readonly IOwnerUsageReader _ownerUsage;
    private readonly IActiveBookingsCounter _bookings;
    private readonly IClock _clock;

    public UserTrashProvider(
        IdentityDbContext dbContext, PrivacyHandlers privacy, AccountAnonymizer anonymizer, IOwnerUsageReader ownerUsage,
        IActiveBookingsCounter bookings, IClock clock)
    {
        _dbContext = dbContext;
        _privacy = privacy;
        _anonymizer = anonymizer;
        _ownerUsage = ownerUsage;
        _bookings = bookings;
        _clock = clock;
    }

    public string Kind => "user";

    public async Task<string?> GetNameAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.FullName).FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<TrashedItem>> MoveToTrashAsync(Guid id, Guid adminId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return Result.Failure<TrashedItem>(TrashErrors.NotFound);
        }

        if (ReviewAccount.Is(user.PhoneNumber))
        {
            return Result.Failure<TrashedItem>(Admin.AdminUserErrors.ReviewAccountProtected);
        }

        // Team accounts are managed (and disabled) from the team accounts page, never deleted from here.
        if (id == adminId || user.Roles.Any(r => _staffRoles.Contains(r, StringComparer.OrdinalIgnoreCase)))
        {
            return Result.Failure<TrashedItem>(TrashErrors.StaffAccount);
        }

        if ((await _ownerUsage.GetAsync(id, cancellationToken)).Venues > 0)
        {
            return Result.Failure<TrashedItem>(TrashErrors.OwnsVenues);
        }

        var upcoming = await _bookings.CountForPlayerAsync(id, cancellationToken);
        if (upcoming > 0)
        {
            return Result.Failure<TrashedItem>(TrashErrors.HasUpcomingBookings(upcoming));
        }

        var deleted = await _privacy.DeleteAccountAsync(id, cancellationToken);
        return deleted.IsFailure
            ? Result.Failure<TrashedItem>(deleted.Error)
            : Result.Success(new TrashedItem(user.FullName, PhoneNumberMasker.Mask(user.PhoneNumber)));
    }

    public async Task<Result> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        var now = _clock.UtcNow;
        if (user is null || !user.CanRestore(now))
        {
            return Result.Failure(TrashErrors.CannotRestore);
        }

        user.Restore(now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PurgeAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _anonymizer.EraseNowAsync(id, cancellationToken) ? Result.Success() : Result.Failure(TrashErrors.NotFound);
}
