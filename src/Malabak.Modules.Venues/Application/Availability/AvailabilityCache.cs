using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Venues.Application.Availability;

/// <summary>Key-value store for cached availability (Redis in production). Implementations may throw; callers fail open.</summary>
public interface IAvailabilityCacheStore
{
    Task<long> GetVersionAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<long> IncrementVersionAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default);
}

public sealed record CachedAvailability(VenueAvailabilityDto Availability, string ETag);

/// <summary>
/// Availability cache (TASK-301). Entries are keyed by a per-venue version that every relevant change increments, so a
/// stale entry is never read after a change commits. TTL is short (and never past the earliest live hold expiry) as a
/// backstop. Concurrent misses for the same key on this instance are coalesced into one computation. Cache errors fall
/// back to computing directly: the cache only ever makes responses faster, never wrong.
/// </summary>
public sealed class CachedVenueAvailability
{
    public static readonly TimeSpan MaxTtl = TimeSpan.FromSeconds(30);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly VenueAvailabilityQuery _query;
    private readonly IAvailabilityCacheStore _store;
    private readonly IClock _clock;
    private readonly ILogger<CachedVenueAvailability> _logger;

    public CachedVenueAvailability(VenueAvailabilityQuery query, IAvailabilityCacheStore store, IClock clock, ILogger<CachedVenueAvailability> logger)
    {
        _query = query;
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<CachedAvailability>> GetAsync(Guid venueId, DateOnly businessDate, Guid? pitchId, CancellationToken cancellationToken = default)
    {
        string? key = null;
        try
        {
            var version = await _store.GetVersionAsync(venueId, cancellationToken);
            key = $"{venueId:N}:v{version}:{businessDate:yyyyMMdd}:{pitchId?.ToString("N") ?? "all"}";
            var hit = await ReadAsync(key, cancellationToken);
            if (hit is not null)
            {
                return Result.Success(hit);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Availability cache unavailable; computing directly");
            return await ComputeAsync(venueId, businessDate, pitchId, null, cancellationToken);
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var hit = await SafeReadAsync(key, cancellationToken);
            return hit is not null ? Result.Success(hit) : await ComputeAsync(venueId, businessDate, pitchId, key, cancellationToken);
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                _locks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, gate));
            }
        }
    }

    private async Task<Result<CachedAvailability>> ComputeAsync(Guid venueId, DateOnly businessDate, Guid? pitchId, string? key, CancellationToken cancellationToken)
    {
        var computed = await _query.GetAsync(venueId, businessDate, pitchId, cancellationToken);
        if (computed.IsFailure)
        {
            return Result.Failure<CachedAvailability>(computed.Error);
        }

        var entry = new CachedAvailability(computed.Value, ETagFor(computed.Value));
        if (key is not null)
        {
            try
            {
                await _store.SetAsync(key, JsonSerializer.Serialize(entry, _json), TtlFor(computed.Value), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not store availability in cache");
            }
        }

        return Result.Success(entry);
    }

    private async Task<CachedAvailability?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var raw = await _store.GetAsync(key, cancellationToken);
        return raw is null ? null : JsonSerializer.Deserialize<CachedAvailability>(raw, _json);
    }

    private async Task<CachedAvailability?> SafeReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Availability cache read failed");
            return null;
        }
    }

    /// <summary>Short TTL with jitter, never past the moment the result changes by itself (hold lapse, notice crossing).</summary>
    private TimeSpan TtlFor(VenueAvailabilityDto availability)
    {
        var ttl = MaxTtl - TimeSpan.FromSeconds(Random.Shared.Next(0, 6));
        if (availability.NextChangeUtc is { } next && next - _clock.UtcNow < ttl)
        {
            ttl = next - _clock.UtcNow;
        }

        return ttl < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : ttl;
    }

    /// <summary>Content hash (ignores the generation timestamp) so unchanged availability keeps the same ETag.</summary>
    public static string ETagFor(VenueAvailabilityDto availability)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(availability with { GeneratedAtUtc = default, NextChangeUtc = null }, _json);
        return "\"" + Convert.ToHexString(SHA256.HashData(content))[..32].ToLowerInvariant() + "\"";
    }
}

/// <summary>Invalidates cached availability; used by the Venues DbContext on save and by other modules (bookings).</summary>
public sealed class AvailabilityInvalidator : IAvailabilityInvalidator
{
    private readonly IAvailabilityCacheStore _store;
    private readonly VenuesDbContext _dbContext;
    private readonly ILogger<AvailabilityInvalidator> _logger;

    public AvailabilityInvalidator(IAvailabilityCacheStore store, VenuesDbContext dbContext, ILogger<AvailabilityInvalidator> logger)
    {
        _store = store;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default)
    {
        var venueId = await _dbContext.Pitches.AsNoTracking().Where(p => p.Id == pitchId).Select(p => (Guid?)p.VenueId).FirstOrDefaultAsync(cancellationToken);
        if (venueId is not null)
        {
            await InvalidateVenueAsync(venueId.Value, cancellationToken);
        }
    }

    public async Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _store.IncrementVersionAsync(venueId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The short TTL bounds staleness if the store is down; booking creation always re-validates against the database.
            _logger.LogError(ex, "Failed to invalidate availability cache for venue {VenueId}", venueId);
        }
    }
}
