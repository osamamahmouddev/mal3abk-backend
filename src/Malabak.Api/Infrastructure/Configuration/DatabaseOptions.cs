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

    /// <summary>
    /// Applies pending migrations when the API starts. For hosts without a release step (e.g. Render's free
    /// tier); where one exists, run <c>--migrate</c> there instead. Off by default.
    /// </summary>
    public bool MigrateOnStartup { get; set; }
}
