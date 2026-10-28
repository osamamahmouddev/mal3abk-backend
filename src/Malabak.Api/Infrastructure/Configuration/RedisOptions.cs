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
}
