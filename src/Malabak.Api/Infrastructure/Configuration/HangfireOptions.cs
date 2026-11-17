using System.ComponentModel.DataAnnotations;

namespace Malabak.Api.Infrastructure.Configuration;

public sealed class HangfireOptions
{
    public const string SectionName = "Hangfire";

    public string? ConnectionString { get; set; }

    [Required(ErrorMessage = "Hangfire schema name is required.")]
    public string SchemaName { get; set; } = "hangfire";

    [Range(1, 100, ErrorMessage = "WorkerCount must be between 1 and 100.")]
    public int WorkerCount { get; set; } = 10;

    public string[] Queues { get; set; } = ["critical", "default", "low"];

    [Required(ErrorMessage = "Dashboard path is required.")]
    public string DashboardPath { get; set; } = "/admin/hangfire";

    public bool EnableDashboard { get; set; } = true;

    public bool BypassDashboardAuthInDevelopment { get; set; }

    public string? CloudflareAccessTeamDomain { get; set; }

    public string? CloudflareAccessAudPolicy { get; set; }

    [Range(0, 20, ErrorMessage = "DefaultRetryAttempts must be between 0 and 20.")]
    public int DefaultRetryAttempts { get; set; } = 5;

    [Range(1, 60, ErrorMessage = "PollIntervalSeconds must be between 1 and 60.")]
    public int PollIntervalSeconds { get; set; } = 5;
}
