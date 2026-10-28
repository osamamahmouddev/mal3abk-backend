using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Hangfire.States;
using Hangfire.Storage;
using Malabak.Api.Infrastructure.Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class HangfireRetryPolicyFilterTests
{
    private const string _connectionString = "Host=localhost;Port=5432;Database=malabak_dev;Username=malabak;Password=malabak_dev_password";
    private readonly HangfireRetryPolicyFilterAttribute _filter = new(
        maxAttempts: 3,
        logger: NullLogger<HangfireRetryPolicyFilterAttribute>.Instance);

    [Theory]
    [InlineData(1, 10, 16)]
    [InlineData(2, 20, 26)]
    [InlineData(3, 40, 46)]
    public void CalculateExponentialBackoff_ForGivenAttempt_ShouldCalculateExpectedRange(
        int attempt,
        int minExpectedSeconds,
        int maxExpectedSeconds)
    {
        // Act
        var delay = HangfireRetryPolicyFilterAttribute.CalculateExponentialBackoff(attempt);

        // Assert
        delay.TotalSeconds.Should().BeInRange(minExpectedSeconds, maxExpectedSeconds);
    }

    [Fact]
    public void CalculateExponentialBackoff_ForHighAttempt_ShouldCapAt30Minutes()
    {
        // Act
        var delay = HangfireRetryPolicyFilterAttribute.CalculateExponentialBackoff(15);

        // Assert
        delay.TotalSeconds.Should().BeLessOrEqualTo(1800);
    }

    [Fact]
    public void OnStateElection_WhenCandidateStateIsNotFailed_ShouldNotModifyCandidateState()
    {
        // Arrange
        var storage = CreateStorage();
        using var connection = storage.GetConnection();
        using var transaction = connection.CreateWriteTransaction();

        var backgroundJob = CreateBackgroundJob(connection);
        var apply = new ApplyStateContext(storage, connection, transaction, backgroundJob, new SucceededState("result", 10, 10), "Processing");
        var context = new ElectStateContext(apply);

        // Act
        _filter.OnStateElection(context);

        // Assert
        context.CandidateState.Should().BeOfType<SucceededState>();
    }

    [Fact]
    public void OnStateElection_WhenCandidateStateIsFailedAndAttemptsRemain_ShouldTransitionToScheduledState()
    {
        // Arrange
        var storage = CreateStorage();
        using var connection = storage.GetConnection();
        using var transaction = connection.CreateWriteTransaction();

        var failedState = new FailedState(new InvalidOperationException("Test background failure"));
        var backgroundJob = CreateBackgroundJob(connection);
        var apply = new ApplyStateContext(storage, connection, transaction, backgroundJob, failedState, "Processing");
        var context = new ElectStateContext(apply);

        // Act
        _filter.OnStateElection(context);

        // Assert
        context.CandidateState.Should().BeOfType<ScheduledState>();
        var scheduled = (ScheduledState)context.CandidateState;
        scheduled.Reason.Should().Contain("Retry attempt 1 of 3");
        context.GetJobParameter<int>("RetryCount").Should().Be(1);
    }

    [Fact]
    public void OnStateElection_WhenCandidateStateIsFailedAndMaxAttemptsReached_ShouldRemainFailedState()
    {
        // Arrange
        var storage = CreateStorage();
        using var connection = storage.GetConnection();
        using var transaction = connection.CreateWriteTransaction();

        var failedState = new FailedState(new InvalidOperationException("Fatal error"));
        var backgroundJob = CreateBackgroundJob(connection);
        var apply = new ApplyStateContext(storage, connection, transaction, backgroundJob, failedState, "Processing");
        var context = new ElectStateContext(apply);
        context.SetJobParameter("RetryCount", 3); // Max is 3

        // Act
        _filter.OnStateElection(context);

        // Assert
        context.CandidateState.Should().BeOfType<FailedState>();
    }

    private static PostgreSqlStorage CreateStorage()
    {
        var options = new PostgreSqlStorageOptions
        {
            SchemaName = "hangfire_test",
            PrepareSchemaIfNecessary = true
        };
#pragma warning disable CS0618
        return new PostgreSqlStorage(_connectionString, options);
#pragma warning restore CS0618
    }

    private static Hangfire.BackgroundJob CreateBackgroundJob(IStorageConnection connection)
    {
        var job = Job.FromExpression(() => HangfireTestJobTarget.SampleJobExecutionTarget());
        var jobId = connection.CreateExpiredJob(job, new Dictionary<string, string>(), DateTime.UtcNow, TimeSpan.FromDays(1));
        return new Hangfire.BackgroundJob(jobId, job, DateTime.UtcNow);
    }
}

public static class HangfireTestJobTarget
{
    public static void SampleJobExecutionTarget()
    {
        // Public target for Hangfire Job expression reflection
    }
}
