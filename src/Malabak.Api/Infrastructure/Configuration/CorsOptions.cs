using System.ComponentModel.DataAnnotations;

namespace Malabak.Api.Infrastructure.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Security:Cors";

    [Required(ErrorMessage = "At least one CORS origin must be configured.")]
    [MinLength(1, ErrorMessage = "AllowedOrigins list cannot be empty.")]
    public string[] AllowedOrigins { get; set; } =
    [
        "http://localhost:3000",
        "http://localhost:5000",
        "http://localhost:5001"
    ];
}
