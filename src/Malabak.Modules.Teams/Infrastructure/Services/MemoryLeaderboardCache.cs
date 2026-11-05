using System.Collections.Concurrent;
using Malabak.Modules.Teams.Application.Common;
using Malabak.SharedKernel;

namespace Malabak.Modules.Teams.Infrastructure.Services;

public sealed class MemoryLeaderboardCache : ILeaderboardCache
{
    private sealed record CacheEntry(PagedList<TeamLeaderboardEntryDto> Data, DateTime ExpiresAtUtc);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IClock _clock;

    public MemoryLeaderboardCache(IClock clock)
    {
        _clock = clock;
    }

    public Task<PagedList<TeamLeaderboardEntryDto>?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAtUtc > _clock.UtcNow)
            {
                return Task.FromResult<PagedList<TeamLeaderboardEntryDto>?>(entry.Data);
            }

            _cache.TryRemove(key, out _);
        }

        return Task.FromResult<PagedList<TeamLeaderboardEntryDto>?>(null);
    }

    public Task SetAsync(string key, PagedList<TeamLeaderboardEntryDto> data, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var entry = new CacheEntry(data, _clock.UtcNow.Add(ttl));
        _cache[key] = entry;
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        _cache.Clear();
        return Task.CompletedTask;
    }
}
