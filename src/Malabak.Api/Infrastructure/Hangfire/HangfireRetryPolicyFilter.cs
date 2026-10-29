using System.Security.Cryptography;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace Malabak.Api.Infrastructure.Hangfire;

/// <summary>
/// Global Hangfire filter that intercepts job failures and applies exponential backoff with jitter
/// up to a maximum attempt count before transitioning the job to the Failed state.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class HangfireRetryPolicyFilterAttribute : JobFilterAttribute, IElectStateFilter, IApplyStateFilter
{
    private const string _retryCountParameter = "RetryCount";
    private readonly int _maxAttempts;
    private readonly ILogger<HangfireRetryPolicyFilterAttribute> _logger;

    public HangfireRetryPolicyFilterAttribute(int maxAttempts, ILogger<HangfireRetryPolicyFilterAttribute> logger)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void OnStateElection(ElectStateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.CandidateState is not FailedState failedState)
        {
            return;
        }

        var currentAttempt = context.GetJobParameter<int>(_retryCountParameter);

        if (currentAttempt < _maxAttempts)
        {
            var nextAttempt = currentAttempt + 1;
            var delay = CalculateExponentialBackoff(nextAttempt);

            context.SetJobParameter(_retryCountParameter, nextAttempt);

            var scheduledState = new ScheduledState(delay)
            {
                Reason = $"Retry attempt {nextAttempt} of {_maxAttempts} scheduled after exponential backoff ({delay.TotalSeconds:F0}s). Exception: {failedState.Exception?.Message}"
            };

            context.CandidateState = scheduledState;

            _logger.LogWarning(
                failedState.Exception,
                "Job '{JobId}' of type '{JobType}' failed (Attempt {Attempt}/{MaxAttempts}). Retrying in {DelaySeconds}s.",
                context.BackgroundJob.Id,
                context.BackgroundJob.Job?.Type.Name ?? "Unknown",
                nextAttempt,
                _maxAttempts,
                delay.TotalSeconds);
        }
        else
        {
            _logger.LogError(
                failedState.Exception,
                "Job '{JobId}' of type '{JobType}' permanently failed after exhausting all {MaxAttempts} retry attempts.",
                context.BackgroundJob.Id,
                context.BackgroundJob.Job?.Type.Name ?? "Unknown",
                _maxAttempts);
        }
    }

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        // No-op
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        // No-op
    }

    public static TimeSpan CalculateExponentialBackoff(int attempt)
    {
        // Formula: 2^(attempt) * 5 seconds + jitter (1-5s), max 1800s (30m)
        var baseSeconds = Math.Pow(2, attempt) * 5;
        var jitterSeconds = RandomNumberGenerator.GetInt32(1, 6);
        var totalSeconds = Math.Min(baseSeconds + jitterSeconds, 1800);

        return TimeSpan.FromSeconds(totalSeconds);
    }
}
