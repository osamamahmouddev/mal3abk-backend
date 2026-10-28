using System.ComponentModel.DataAnnotations;

namespace Malabak.Api.Infrastructure.Configuration;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    [Range(1, 20)]
    public int MaxRetries { get; init; } = 5;

    [Range(1, 300)]
    public int PollIntervalSeconds { get; init; } = 5;

    [Range(1, 600)]
    public int DistributedLockTimeoutSeconds { get; init; } = 30;

    public bool Enabled { get; init; } = true;
}
