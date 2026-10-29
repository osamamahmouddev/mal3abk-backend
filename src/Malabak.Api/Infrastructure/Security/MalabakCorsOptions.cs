namespace Malabak.Api.Infrastructure.Security;

public sealed class MalabakCorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "DefaultCorsPolicy";

    public List<string> AllowedOrigins { get; set; } =
    [
        "https://malabak.app",
        "https://venue.malabak.app",
        "https://admin.malabak.app",
        "http://localhost:3000",
        "http://localhost:3001"
    ];

    public int PreflightMaxAgeHours { get; set; } = 1;
}
