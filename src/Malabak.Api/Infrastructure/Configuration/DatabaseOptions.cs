using System.ComponentModel.DataAnnotations;

namespace Malabak.Api.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required(ErrorMessage = "Database connection string is required.")]
    public string ConnectionString { get; set; } = string.Empty;

    [Range(5, 500, ErrorMessage = "MaxPoolSize must be between 5 and 500.")]
    public int MaxPoolSize { get; set; } = 100;

    [Range(1, 120, ErrorMessage = "CommandTimeoutSeconds must be between 1 and 120.")]
    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool EnableSensitiveDataLogging { get; set; }
}
