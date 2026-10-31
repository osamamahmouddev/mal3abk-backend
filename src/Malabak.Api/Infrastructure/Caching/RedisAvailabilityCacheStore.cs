using Malabak.Modules.Venues.Application.Availability;
using Malabak.SharedKernel.Caching;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Caching;

/// <summary>Redis-backed availability cache: one version counter per venue plus short-lived JSON entries.</summary>
public sealed class RedisAvailabilityCacheStore : IAvailabilityCacheStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IRedisKeyBuilder _keys;

    public RedisAvailabilityCacheStore(IConnectionMultiplexer redis, IRedisKeyBuilder keys)
    {
        _redis = redis;
        _keys = keys;
    }

    public async Task<long> GetVersionAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(VersionKey(venueId));
        return value.HasValue && long.TryParse(value.ToString(), out var version) ? version : 0;
    }

    public async Task<IReadOnlyDictionary<Guid, long>> GetVersionsAsync(IReadOnlyCollection<Guid> venueIds, CancellationToken cancellationToken = default)
    {
        var ids = venueIds.ToArray();
        var values = await _redis.GetDatabase().StringGetAsync(ids.Select(VersionKey).ToArray()); // one MGET
        var versions = new Dictionary<Guid, long>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            versions[ids[i]] = values[i].HasValue && long.TryParse(values[i].ToString(), out var version) ? version : 0;
        }

        return versions;
    }

    public Task<long> IncrementVersionAsync(Guid venueId, CancellationToken cancellationToken = default) =>
        _redis.GetDatabase().StringIncrementAsync(VersionKey(venueId));

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(EntryKey(key));
        return value.HasValue ? value.ToString() : null;
    }

    public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        _redis.GetDatabase().StringSetAsync(EntryKey(key), value, ttl);

    private RedisKey VersionKey(Guid venueId) => _keys.Format("venues", "availability-version", venueId.ToString("N"));

    private RedisKey EntryKey(string key) => _keys.Format("venues", "availability", key.Split(':'));
}
