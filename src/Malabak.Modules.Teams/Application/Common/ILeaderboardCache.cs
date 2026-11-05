namespace Malabak.Modules.Teams.Application.Common;

public interface ILeaderboardCache
{
    Task<PagedList<TeamLeaderboardEntryDto>?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, PagedList<TeamLeaderboardEntryDto> data, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}
