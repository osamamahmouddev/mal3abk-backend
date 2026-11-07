using System.Text.Json;
using Malabak.Modules.Identity.Application.Admin;
using StackExchange.Redis;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Impersonation codes in Redis (SECURITY.md: 60 s, single use). The key is the SHA-256 of the code; GETDEL makes the
/// exchange atomic, so two concurrent exchanges of one code can never both succeed. No in-memory fallback: fails closed.
/// </summary>
public sealed class RedisImpersonationCodeStore : IImpersonationCodeStore
{
    private const string _prefix = "impersonation:code:";

    private readonly IConnectionMultiplexer _redis;

    public RedisImpersonationCodeStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public Task SaveAsync(string codeHash, ImpersonationGrant grant, TimeSpan lifetime, CancellationToken cancellationToken = default) =>
        _redis.GetDatabase().StringSetAsync(_prefix + codeHash, JsonSerializer.Serialize(grant), lifetime, When.NotExists);

    public async Task<ImpersonationGrant?> TakeAsync(string codeHash, CancellationToken cancellationToken = default)
    {
        var value = await _redis.GetDatabase().StringGetDeleteAsync(_prefix + codeHash);
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<ImpersonationGrant>(value.ToString());
    }
}
