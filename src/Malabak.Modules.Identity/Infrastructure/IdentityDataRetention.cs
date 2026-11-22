using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Infrastructure;

/// <summary>Refresh tokens long expired (ADR 0007: 30 days), whether rotated, revoked or simply unused.</summary>
public sealed class RefreshTokenRetention : IDataRetentionTask
{
    private readonly IdentityDbContext _dbContext;

    public RefreshTokenRetention(IdentityDbContext dbContext) => _dbContext = dbContext;

    public string Name => "identity.refresh_tokens";

    public Task<int> PurgeAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var cutoff = nowUtc - DataRetentionRules.RefreshTokensAfterExpiry;
        return _dbContext.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.ExpiresAtUtc < cutoff)
            .OrderBy(t => t.ExpiresAtUtc)
            .DeleteInBatchesAsync(cancellationToken);
    }
}

/// <summary>
/// Device sessions unused for 90 days whose refresh chain can no longer be used (no live token left in the family), so
/// nobody is signed out by this: the session was already dead.
/// </summary>
public sealed class InactiveSessionRetention : IDataRetentionTask
{
    private readonly IdentityDbContext _dbContext;

    public InactiveSessionRetention(IdentityDbContext dbContext) => _dbContext = dbContext;

    public string Name => "identity.user_sessions";

    public Task<int> PurgeAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var cutoff = nowUtc - DataRetentionRules.InactiveSessions;
        return _dbContext.UserSessions.IgnoreQueryFilters()
            .Where(s => s.LastActivityAtUtc < cutoff
                && !_dbContext.RefreshTokens.IgnoreQueryFilters().Any(t => t.FamilyId == s.Id && !t.IsRevoked && t.ExpiresAtUtc > nowUtc))
            .OrderBy(s => s.LastActivityAtUtc)
            .DeleteInBatchesAsync(cancellationToken);
    }
}

/// <summary>SMS/WhatsApp delivery logs older than 90 days (the daily and monthly spend caps only read the current month).</summary>
public sealed class SmsDispatchRetention : IDataRetentionTask
{
    private readonly IdentityDbContext _dbContext;

    public SmsDispatchRetention(IdentityDbContext dbContext) => _dbContext = dbContext;

    public string Name => "identity.sms_dispatches";

    public Task<int> PurgeAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var cutoff = nowUtc - DataRetentionRules.SmsDispatches;
        return _dbContext.SmsDispatches
            .Where(d => d.CreatedAtUtc < cutoff)
            .OrderBy(d => d.CreatedAtUtc)
            .DeleteInBatchesAsync(cancellationToken);
    }
}
