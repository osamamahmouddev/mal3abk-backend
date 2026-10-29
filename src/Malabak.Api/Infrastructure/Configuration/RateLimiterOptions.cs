using System.ComponentModel.DataAnnotations;

namespace Malabak.Api.Infrastructure.Configuration;

public sealed class RateLimiterOptions
{
    public const string SectionName = "Security:RateLimiter";

    [Range(1, 10000, ErrorMessage = "PermitLimit must be between 1 and 10000.")]
    public int PermitLimit { get; set; } = 100;

    [Range(1, 3600, ErrorMessage = "WindowSeconds must be between 1 and 3600.")]
    public int WindowSeconds { get; set; } = 60;

    [Range(0, 1000, ErrorMessage = "QueueLimit must be between 0 and 1000.")]
    public int QueueLimit { get; set; } = 10;
}
