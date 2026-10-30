using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Privacy;

public sealed record AccountDeletionDto(DateTime DeletionRequestedAtUtc, DateTime PersonalDataErasedAfterUtc);

public sealed record ConsentDto(string Type, bool Granted, string? Version, DateTime RecordedAtUtc);

public sealed record ConsentsDto(IReadOnlyList<ConsentDto> Current, IReadOnlyList<ConsentDto> History);

public static class PrivacyErrors
{
    public static readonly Error NotFound = new("Privacy.NotFound", "Account not found.");
    public static readonly Error InvalidConsent = new("Privacy.InvalidConsent",
        "Choose a valid consent type; terms and privacy acceptance must include the document version (max 20 characters).");
}

/// <summary>
/// Data-subject rights (Law 151/2020, SECURITY.md §17.2): account deletion (step-up protected at the endpoint),
/// consent records and a personal data export covering every module through <see cref="IPersonalDataContributor"/>.
/// </summary>
public sealed class PrivacyHandlers
{
    private readonly IdentityDbContext _dbContext;
    private readonly ISessionStore _sessions;
    private readonly IEnumerable<IPersonalDataContributor> _contributors;
    private readonly IClock _clock;
    private readonly ILogger<PrivacyHandlers> _logger;

    public PrivacyHandlers(
        IdentityDbContext dbContext, ISessionStore sessions, IEnumerable<IPersonalDataContributor> contributors, IClock clock, ILogger<PrivacyHandlers> logger)
    {
        _dbContext = dbContext;
        _sessions = sessions;
        _contributors = contributors;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Soft delete (ADR-0007 phase 1): signs out every device and closes the account. Nothing is erased yet because
    /// the owner may restore the account by OTP during the grace period; <see cref="AccountAnonymizer"/> erases the
    /// account and every module's data once the window has passed.
    /// </summary>
    public async Task<Result<AccountDeletionDto>> DeleteAccountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AccountDeletionDto>(PrivacyErrors.NotFound);
        }

        var nowUtc = _clock.UtcNow;
        user.RequestDeletion(nowUtc);

        var sessions = await _dbContext.UserSessions.Where(s => s.UserId == userId && !s.IsRevoked).ToListAsync(cancellationToken);
        sessions.ForEach(s => s.Revoke(nowUtc, "account_deleted"));
        var tokens = await _dbContext.RefreshTokens.Where(t => t.UserId == userId && !t.IsRevoked).ToListAsync(cancellationToken);
        tokens.ForEach(t => t.Revoke("account_deleted", nowUtc));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _sessions.MarkRevokedAsync(sessions.Select(s => s.Id).ToList(), cancellationToken);

        _logger.LogInformation("Account {UserId} deleted; personal data erased after {Days} days", userId, User.DeletionRetention.TotalDays);
        return Result.Success(new AccountDeletionDto(nowUtc, nowUtc + User.DeletionRetention));
    }

    public async Task<Result<ConsentsDto>> RecordConsentAsync(
        Guid userId, ConsentType type, bool granted, string? version, CancellationToken cancellationToken = default)
    {
        var needsVersion = granted && type is ConsentType.Terms or ConsentType.Privacy;
        if (!Enum.IsDefined(type) || (needsVersion && string.IsNullOrWhiteSpace(version)) || version?.Trim().Length > UserConsent.MaxVersionLength)
        {
            return Result.Failure<ConsentsDto>(PrivacyErrors.InvalidConsent);
        }

        if (!await _dbContext.Users.AnyAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken))
        {
            return Result.Failure<ConsentsDto>(PrivacyErrors.NotFound);
        }

        _dbContext.UserConsents.Add(UserConsent.Record(userId, type, granted, version, _clock.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(await GetConsentsAsync(userId, cancellationToken));
    }

    public async Task<ConsentsDto> GetConsentsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var history = (await _dbContext.UserConsents.AsNoTracking().Where(c => c.UserId == userId).ToListAsync(cancellationToken))
            .OrderByDescending(c => c.RecordedAtUtc)
            .ThenByDescending(c => c.Id)
            .Select(c => new ConsentDto(c.Type.ToString(), c.Granted, c.Version, c.RecordedAtUtc))
            .ToList();
        var current = history.GroupBy(c => c.Type).Select(g => g.First()).OrderBy(c => c.Type, StringComparer.Ordinal).ToList();
        return new ConsentsDto(current, history);
    }

    /// <summary>Everything we hold about the user, as one JSON document. Secrets (hashes, tokens, TOTP) are never included.</summary>
    public async Task<Result<IReadOnlyDictionary<string, object?>>> ExportAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return Result.Failure<IReadOnlyDictionary<string, object?>>(PrivacyErrors.NotFound);
        }

        var sessions = await _dbContext.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new { s.DeviceName, s.IpAddress, s.UserAgent, s.CreatedAtUtc, s.LastActivityAtUtc, s.IsRevoked })
            .ToListAsync(cancellationToken);

        var export = new Dictionary<string, object?>
        {
            ["exportedAtUtc"] = _clock.UtcNow,
            ["account"] = new
            {
                user.Id,
                user.PhoneNumber,
                user.Email,
                user.FullName,
                user.City,
                user.Area,
                Positions = user.Positions.Select(p => p.ToString()).ToList(),
                SkillLevel = user.SkillLevel?.ToString(),
                user.Language,
                user.AvatarUrl,
                user.Roles,
                user.TwoFactorEnabled,
                user.CreatedAtUtc,
                user.OnboardingCompletedAtUtc
            },
            ["consents"] = (await GetConsentsAsync(userId, cancellationToken)).History,
            ["sessions"] = sessions
        };

        foreach (var contributor in _contributors)
        {
            export[contributor.Section] = await contributor.ExportAsync(userId, cancellationToken);
        }

        return Result.Success<IReadOnlyDictionary<string, object?>>(export);
    }

    internal async Task EraseContributorsAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (var contributor in _contributors)
        {
            try
            {
                await contributor.EraseAsync(userId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The anonymization job erases again later, so a failure here is retried rather than fatal.
                _logger.LogWarning(ex, "Erasing {Section} data for {UserId} failed", contributor.Section, userId);
            }
        }
    }
}

/// <summary>
/// Erases personal data of accounts deleted more than <see cref="User.DeletionRetention"/> ago. Bookings and
/// payment records stay (accounting) but now point to an anonymous user. Idempotent and batch-limited.
/// </summary>
public sealed class AccountAnonymizer
{
    public const int BatchSize = 100;

    private readonly IdentityDbContext _dbContext;
    private readonly PrivacyHandlers _privacy;
    private readonly IClock _clock;

    public AccountAnonymizer(IdentityDbContext dbContext, PrivacyHandlers privacy, IClock clock)
    {
        _dbContext = dbContext;
        _privacy = privacy;
        _clock = clock;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var cutoff = nowUtc - User.DeletionRetention;
        // Deleted accounts are hidden by the soft-delete query filter; they are exactly the ones this job needs.
        var due = await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsDeleted && u.AnonymizedAtUtc == null && u.DeletionRequestedAtUtc != null && u.DeletionRequestedAtUtc <= cutoff)
            .OrderBy(u => u.DeletionRequestedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var user in due)
        {
            await _privacy.EraseContributorsAsync(user.Id, cancellationToken);

            user.Anonymize(nowUtc);

            // Physically removed (not soft-deleted): sessions and phone-change requests hold IPs, devices and numbers.
            _dbContext.HardDelete(await _dbContext.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == user.Id).ToListAsync(cancellationToken));
            _dbContext.HardDelete(await _dbContext.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id).ToListAsync(cancellationToken));
            _dbContext.UserActionTokens.RemoveRange(await _dbContext.UserActionTokens.Where(t => t.UserId == user.Id).ToListAsync(cancellationToken));
            _dbContext.HardDelete(await _dbContext.PhoneChangeRequests.IgnoreQueryFilters().Where(r => r.UserId == user.Id).ToListAsync(cancellationToken));
            _dbContext.AdminRecoveryCodes.RemoveRange(await _dbContext.AdminRecoveryCodes.Where(c => c.UserId == user.Id).ToListAsync(cancellationToken));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }
}
