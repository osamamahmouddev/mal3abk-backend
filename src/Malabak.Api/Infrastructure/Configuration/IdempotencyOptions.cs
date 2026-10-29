using System.ComponentModel.DataAnnotations;

namespace Malabak.Api.Infrastructure.Configuration;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    [Required]
    [MinLength(1)]
    public string HeaderName { get; init; } = "Idempotency-Key";

    [Range(1, 168)] // Up to 7 days, default 24 hours per PRD & SECURITY.md §8.4
    public int ExpirationHours { get; init; } = 24;

    [Range(1, 60)]
    public int LockTimeoutSeconds { get; init; } = 10;

    [Range(1, 120)]
    public int LockDurationSeconds { get; init; } = 30;
}
