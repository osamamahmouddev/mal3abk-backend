namespace Malabak.Modules.Identity.Application.Common;

public sealed class JwtOptions
{
    public const string SectionName = "Security:Jwt";

    public string Issuer { get; set; } = "Malabak.Api";
    public string Audience { get; set; } = "Malabak.Client";
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;
    public string CurrentKeyId { get; set; } = "malabak-es256-key-v1";
    public string? PrivateKeyPem { get; set; }
}
