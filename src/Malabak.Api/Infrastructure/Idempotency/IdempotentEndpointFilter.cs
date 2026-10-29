using System.Text.Json;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Caching;
using Malabak.SharedKernel.Locking;
using Malabak.SharedKernel.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Idempotency;

public sealed class IdempotentEndpointFilter : IEndpointFilter
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IIdempotencyStore _store;
    private readonly IRequestHasher _hasher;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IRedisKeyBuilder _keyBuilder;
    private readonly IClock _clock;
    private readonly IOptions<IdempotencyOptions> _options;
    private readonly ILogger<IdempotentEndpointFilter> _logger;

    public IdempotentEndpointFilter(
        IIdempotencyStore store,
        IRequestHasher hasher,
        IDistributedLockProvider lockProvider,
        IRedisKeyBuilder keyBuilder,
        IClock clock,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotentEndpointFilter> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _keyBuilder = keyBuilder ?? throw new ArgumentNullException(nameof(keyBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var metadata = httpContext.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>();
        var isRequired = metadata?.Required ?? true;

        var headerName = _options.Value.HeaderName;
        if (!httpContext.Request.Headers.TryGetValue(headerName, out var keyValues) || string.IsNullOrWhiteSpace(keyValues.ToString()))
        {
            if (isRequired)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Idempotency-Key Required",
                    detail: $"The '{headerName}' request header is required for this operation.",
                    extensions: new Dictionary<string, object?> { ["code"] = "idempotency_key_required" });
            }

            return await next(context);
        }

        var key = keyValues.ToString().Trim();
        if (key.Length > 256)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Idempotency-Key",
                detail: "Idempotency-Key header value exceeds the 256 character limit.",
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_idempotency_key" });
        }

        var requestHash = await _hasher.ComputeHashAsync(httpContext.Request, httpContext.RequestAborted);

        var lockResource = _keyBuilder.Format("idempotency", "lock", key);
        var lockDuration = TimeSpan.FromSeconds(_options.Value.LockDurationSeconds);
        var lockTimeout = TimeSpan.FromSeconds(_options.Value.LockTimeoutSeconds);

        var lockHandle = await _lockProvider.TryAcquireLockAsync(
            lockResource,
            lockDuration,
            lockTimeout,
            httpContext.RequestAborted);

        if (lockHandle is null)
        {
            _logger.LogWarning("Concurrent request with Idempotency-Key {Key} timed out waiting for lock.", key);
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Concurrent Request Conflict",
                detail: "A request with this Idempotency-Key is currently being processed. Please retry shortly.",
                extensions: new Dictionary<string, object?> { ["code"] = "idempotency_conflict" });
        }

        await using (lockHandle)
        {
            var existingRecord = await _store.GetAsync(key, httpContext.RequestAborted);
            if (existingRecord is not null)
            {
                if (existingRecord.IsExpired(_clock.UtcNow))
                {
                    await _store.DeleteAsync(key, httpContext.RequestAborted);
                }
                else
                {
                    if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
                    {
                        _logger.LogWarning("Idempotency key {Key} payload mismatch. Stored: {StoredHash}, Incoming: {IncomingHash}",
                            key, existingRecord.RequestHash, requestHash);

                        return Results.Problem(
                            statusCode: StatusCodes.Status422UnprocessableEntity,
                            title: "Unprocessable Entity",
                            detail: "Idempotency-Key already used with a different request payload.",
                            extensions: new Dictionary<string, object?> { ["code"] = "idempotency_body_mismatch" });
                    }

                    if (existingRecord.Status == IdempotencyStatus.Completed)
                    {
                        _logger.LogInformation("Replaying stored response for Idempotency-Key {Key}", key);
                        httpContext.Response.Headers["Idempotent-Replayed"] = "true";

                        var contentType = existingRecord.ResponseContentType ?? "application/json";
                        return Results.Content(existingRecord.ResponseBody, contentType, statusCode: existingRecord.StatusCode);
                    }

                    if (existingRecord.Status == IdempotencyStatus.Pending)
                    {
                        return Results.Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            title: "Concurrent Request Conflict",
                            detail: "A request with this Idempotency-Key is currently being processed.",
                            extensions: new Dictionary<string, object?> { ["code"] = "idempotency_conflict" });
                    }
                }
            }

            var newRecord = new IdempotencyRecord
            {
                Key = key,
                RequestHash = requestHash,
                HttpMethod = httpContext.Request.Method.ToUpperInvariant(),
                RequestPath = httpContext.Request.Path.Value ?? string.Empty,
                CreatedAtUtc = _clock.UtcNow,
                ExpiresAtUtc = _clock.UtcNow.AddHours(_options.Value.ExpirationHours)
            };

            await _store.CreatePendingAsync(newRecord, httpContext.RequestAborted);

            object? result;
            try
            {
                result = await next(context);
            }
            catch (Exception)
            {
                await _store.DeleteAsync(key, CancellationToken.None);
                throw;
            }

            var statusCode = StatusCodes.Status200OK;
            string? responseContentType = null;
            string? responseBody = null;

            if (result is IStatusCodeHttpResult statusResult && statusResult.StatusCode.HasValue)
            {
                statusCode = statusResult.StatusCode.Value;
            }

            if (result is IValueHttpResult valueResult)
            {
                responseBody = JsonSerializer.Serialize(valueResult.Value, _jsonOptions);
                responseContentType = "application/json";
            }
            else if (result is ContentHttpResult contentResult)
            {
                responseBody = contentResult.ResponseContent;
                responseContentType = contentResult.ContentType ?? "application/json";
            }
            else if (result is not null && result is not IResult)
            {
                responseBody = JsonSerializer.Serialize(result, _jsonOptions);
                responseContentType = "application/json";
            }

            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                await _store.DeleteAsync(key, CancellationToken.None);
            }
            else
            {
                await _store.CompleteAsync(key, statusCode, responseContentType, responseBody, CancellationToken.None);
            }

            return result;
        }
    }
}
