using System.Text.Json;
using Malabak.SharedKernel.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Malabak.Api.Infrastructure.Redis;

/// <summary>
/// Implements atomic single-use consume operations using Redis Lua scripts.
/// Guarantees that tokens (OTP, WS ticket, Impersonation) can only be read and consumed once.
/// </summary>
public sealed class RedisAtomicConsumeStore : IAtomicConsumeStore
{
    private const string _atomicConsumeScript = @"
        local val = redis.call('get', KEYS[1])
        if val then
            redis.call('del', KEYS[1])
        end
        return val";

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisAtomicConsumeStore> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public RedisAtomicConsumeStore(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisAtomicConsumeStore> logger)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SetAsync(
        string key,
        string value,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        ArgumentNullException.ThrowIfNull(value);

        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentException("Expiry must be greater than zero.", nameof(expiry));
        }

        var database = _multiplexer.GetDatabase();

        await FailClosedPolicy.ExecuteSensitiveAsync(
            "SetAtomicToken",
            key,
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var success = await database.StringSetAsync(key, value, expiry).ConfigureAwait(false);
                if (!success)
                {
                    throw new RedisException($"Failed to store key '{key}' in Redis.");
                }

                return true;
            },
            _logger,
            cancellationToken).ConfigureAwait(false);
    }

    public Task SetJsonAsync<T>(
        string key,
        T value,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        return SetAsync(key, json, expiry, cancellationToken);
    }

    public async Task<string?> ConsumeAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        var database = _multiplexer.GetDatabase();

        return await FailClosedPolicy.ExecuteSensitiveAsync(
            "AtomicConsume",
            key,
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await database.ScriptEvaluateAsync(
                    _atomicConsumeScript,
                    new RedisKey[] { key }).ConfigureAwait(false);

                if (result.IsNull)
                {
                    return null;
                }

                return (string?)result;
            },
            _logger,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> ConsumeJsonAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var raw = await ConsumeAsync(key, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(raw, _jsonOptions);
    }

    public async Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        var database = _multiplexer.GetDatabase();

        return await FailClosedPolicy.ExecuteSensitiveAsync(
            "KeyExists",
            key,
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await database.KeyExistsAsync(key).ConfigureAwait(false);
            },
            _logger,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> InvalidateAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        var database = _multiplexer.GetDatabase();

        return await FailClosedPolicy.ExecuteSensitiveAsync(
            "KeyInvalidate",
            key,
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await database.KeyDeleteAsync(key).ConfigureAwait(false);
            },
            _logger,
            cancellationToken).ConfigureAwait(false);
    }
}
