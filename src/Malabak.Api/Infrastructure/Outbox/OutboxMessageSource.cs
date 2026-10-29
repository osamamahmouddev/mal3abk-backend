using System.Security.Cryptography;
using System.Text.Json;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Events;
using Malabak.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Malabak.Api.Infrastructure.Outbox;

internal static class OutboxSerializationDefaults
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public class OutboxMessageSource<TDbContext> : IOutboxMessageSource
    where TDbContext : BaseDbContext
{
    private readonly TDbContext _dbContext;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly IOptions<OutboxOptions> _options;
    private readonly ILogger<OutboxMessageSource<TDbContext>> _logger;

    public string SourceName => typeof(TDbContext).Name;

    public OutboxMessageSource(
        TDbContext dbContext,
        IDomainEventDispatcher dispatcher,
        IClock clock,
        IOptions<OutboxOptions> options,
        ILogger<OutboxMessageSource<TDbContext>> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> DispatchPendingMessagesAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var messages = await _dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null && (m.NextRetryUtc == null || m.NextRetryUtc <= now))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return 0;
        }

        var successfullyDispatchedCount = 0;

        foreach (var message in messages)
        {
            var eventType = ResolveType(message.Type);
            if (eventType is null)
            {
                _logger.LogError("Unable to resolve event type {Type} for outbox message {MessageId}", message.Type, message.Id);
                message.MarkAsFailed($"Unable to resolve event type: {message.Type}", nextRetryUtc: null);
                OutboxMetrics.RecordFailed(message.Type, "TypeLoadException", isDeadLetter: true);
                continue;
            }

            IDomainEvent? domainEvent;
            try
            {
                domainEvent = (IDomainEvent?)JsonSerializer.Deserialize(message.Content, eventType, OutboxSerializationDefaults.JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize outbox message {MessageId} into {Type}", message.Id, message.Type);
                message.MarkAsFailed($"Deserialization error: {ex.Message}", nextRetryUtc: null);
                OutboxMetrics.RecordFailed(message.Type, ex.GetType().Name, isDeadLetter: true);
                continue;
            }

            if (domainEvent is null)
            {
                _logger.LogError("Deserialized payload was null for outbox message {MessageId} of type {Type}", message.Id, message.Type);
                message.MarkAsFailed("Deserialized payload was null", nextRetryUtc: null);
                OutboxMetrics.RecordFailed(message.Type, "NullPayloadException", isDeadLetter: true);
                continue;
            }

            try
            {
                await _dispatcher.DispatchAsync(domainEvent, cancellationToken);
                message.MarkAsProcessed(_clock.UtcNow);
                OutboxMetrics.RecordDispatched(message.Type);
                successfullyDispatchedCount++;
            }
            catch (Exception ex)
            {
                var nextAttempt = message.RetryCount + 1;
                var maxRetries = _options.Value.MaxRetries;

                if (nextAttempt >= maxRetries)
                {
                    _logger.LogError(ex,
                        "Outbox message {MessageId} exceeded maximum retry attempts ({MaxRetries}). Dead-lettering.",
                        message.Id, maxRetries);
                    message.MarkAsFailed($"Exceeded max retries: {ex.Message}", nextRetryUtc: null);
                    OutboxMetrics.RecordFailed(message.Type, ex.GetType().Name, isDeadLetter: true);
                }
                else
                {
                    var delay = CalculateExponentialBackoff(nextAttempt);
                    var nextRetry = _clock.UtcNow.Add(delay);

                    _logger.LogWarning(ex,
                        "Outbox message {MessageId} failed attempt {Attempt}/{MaxRetries}. Scheduling retry in {DelaySeconds:F1}s.",
                        message.Id, nextAttempt, maxRetries, delay.TotalSeconds);
                    message.MarkAsFailed(ex.ToString(), nextRetryUtc: nextRetry);
                    OutboxMetrics.RecordFailed(message.Type, ex.GetType().Name, isDeadLetter: false);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return successfullyDispatchedCount;
    }

    public static TimeSpan CalculateExponentialBackoff(int attempt)
    {
        var baseSeconds = Math.Pow(2, attempt) * 2;
        var jitterMs = RandomNumberGenerator.GetInt32(0, 2000);
        var totalSeconds = Math.Min(baseSeconds + (jitterMs / 1000.0), 1800);
        return TimeSpan.FromSeconds(totalSeconds);
    }

    private static Type? ResolveType(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type is not null)
        {
            return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }
}
