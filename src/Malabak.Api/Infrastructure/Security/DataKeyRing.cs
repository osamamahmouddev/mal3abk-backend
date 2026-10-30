using System.Security.Cryptography;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// Process-wide cache of unwrapped data keys. Keys are loaded lazily (so the API can start without the
/// database) and reloaded periodically so a rotation done by another node is picked up.
/// </summary>
public sealed class DataKeyRing : IDataKeyRing, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKeyEncryptionKeyProvider _kekProvider;
    private readonly IClock _clock;
    private readonly ILogger<DataKeyRing> _logger;
    private readonly TimeSpan _refreshInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Snapshot? _snapshot;

    public DataKeyRing(
        IServiceScopeFactory scopeFactory,
        IKeyEncryptionKeyProvider kekProvider,
        IClock clock,
        IOptions<EncryptionOptions> options,
        ILogger<DataKeyRing> logger)
    {
        _scopeFactory = scopeFactory;
        _kekProvider = kekProvider;
        _clock = clock;
        _logger = logger;
        _refreshInterval = TimeSpan.FromMinutes(options.Value.KeyRingRefreshMinutes);
    }

    public async ValueTask<DataKey> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _snapshot;
        if (snapshot is null || snapshot.CurrentKeyId is null || IsStale(snapshot))
        {
            snapshot = await ReloadAsync(ensureActiveKey: true, cancellationToken);
        }

        var currentId = snapshot.CurrentKeyId
            ?? throw new CryptographicException("No active data encryption key is available.");
        return new DataKey(currentId, snapshot.Keys[currentId]);
    }

    public async ValueTask<byte[]> GetKeyAsync(int keyId, CancellationToken cancellationToken = default)
    {
        // Key material never changes once created, so a cached key is valid regardless of staleness.
        if (_snapshot?.Keys.TryGetValue(keyId, out var cached) == true)
        {
            return cached;
        }

        var snapshot = await ReloadAsync(ensureActiveKey: false, cancellationToken);
        return snapshot.Keys.TryGetValue(keyId, out var key)
            ? key
            : throw new CryptographicException("The data encryption key for this value is unknown.");
    }

    public async Task<int> RotateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
            var nowUtc = _clock.UtcNow;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Retire first in its own statement so the single-active unique index never sees two active rows.
            var active = await db.DataEncryptionKeys.Where(k => k.IsActive).ToListAsync(cancellationToken);
            active.ForEach(k => k.Retire(nowUtc));
            await db.SaveChangesAsync(cancellationToken);

            var created = NewDataKey(nowUtc);
            db.DataEncryptionKeys.Add(created);
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _snapshot = null;
            return created.Id;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RewrapWithCurrentKeyEncryptionKeyAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
            var currentKekId = _kekProvider.CurrentKeyId;
            var nowUtc = _clock.UtcNow;

            var outdated = await db.DataEncryptionKeys
                .Where(k => k.KeyEncryptionKeyId != currentKekId)
                .ToListAsync(cancellationToken);

            foreach (var key in outdated)
            {
                var plainKey = _kekProvider.UnwrapKey(key.KeyEncryptionKeyId, key.WrappedKey);
                try
                {
                    key.Rewrap(_kekProvider.WrapKey(plainKey), currentKekId, nowUtc);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainKey);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return outdated.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private bool IsStale(Snapshot snapshot) => _clock.UtcNow - snapshot.LoadedAtUtc >= _refreshInterval;

    private async Task<Snapshot> ReloadAsync(bool ensureActiveKey, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();

            var rows = await db.DataEncryptionKeys.AsNoTracking().ToListAsync(cancellationToken);
            if (ensureActiveKey && !rows.Exists(k => k.IsActive))
            {
                rows = await CreateInitialKeyAsync(db, cancellationToken);
            }

            // A key that can't be unwrapped (its KEK is gone) only makes its own values unreadable. It must
            // not stop the rest of the ring from encrypting and decrypting.
            var keys = new Dictionary<int, byte[]>();
            foreach (var row in rows)
            {
                try
                {
                    keys[row.Id] = _kekProvider.UnwrapKey(row.KeyEncryptionKeyId, row.WrappedKey);
                }
                catch (CryptographicException ex)
                {
                    _logger.LogError(ex, "Data encryption key {KeyId} can't be unwrapped with key-encryption key {KeyEncryptionKeyId}; values encrypted with it stay unreadable.",
                        row.Id, row.KeyEncryptionKeyId);
                }
            }

            var currentId = rows.Find(k => k.IsActive && keys.ContainsKey(k.Id))?.Id;
            var snapshot = new Snapshot(keys, currentId, _clock.UtcNow);
            _snapshot = snapshot;
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<DataEncryptionKey>> CreateInitialKeyAsync(SecurityDbContext db, CancellationToken cancellationToken)
    {
        db.DataEncryptionKeys.Add(NewDataKey(_clock.UtcNow));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another node created the active key first; use theirs.
            db.ChangeTracker.Clear();
        }

        return await db.DataEncryptionKeys.AsNoTracking().ToListAsync(cancellationToken);
    }

    private DataEncryptionKey NewDataKey(DateTime nowUtc)
    {
        var plainKey = RandomNumberGenerator.GetBytes(EncryptionOptions.KeySizeBytes);
        try
        {
            return DataEncryptionKey.Create(_kekProvider.WrapKey(plainKey), _kekProvider.CurrentKeyId, nowUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainKey);
        }
    }

    private sealed record Snapshot(IReadOnlyDictionary<int, byte[]> Keys, int? CurrentKeyId, DateTime LoadedAtUtc);
}
