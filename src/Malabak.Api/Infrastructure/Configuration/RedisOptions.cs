using System.ComponentModel.DataAnnotations;

namespace Malabak.Api.Infrastructure.Configuration;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required(ErrorMessage = "Redis connection string is required.")]
    public string ConnectionString { get; set; } = string.Empty;

    [Range(100, 30000, ErrorMessage = "ConnectTimeoutMs must be between 100 and 30000 ms.")]
    public int ConnectTimeoutMs { get; set; } = 5000;

    [Range(100, 30000, ErrorMessage = "SyncTimeoutMs must be between 100 and 30000 ms.")]
    public int SyncTimeoutMs { get; set; } = 5000;

    public bool AbortOnConnectFail { get; set; }

    public string KeyPrefix { get; set; } = "malabak";

    public string? EnvironmentName { get; set; }

    public bool? Ssl { get; set; }

    public string? Password { get; set; }

    [Range(0, 15, ErrorMessage = "DefaultDatabase must be between 0 and 15.")]
    public int? DefaultDatabase { get; set; }

    public string ClientName { get; set; } = "Malabak.Api";
}
