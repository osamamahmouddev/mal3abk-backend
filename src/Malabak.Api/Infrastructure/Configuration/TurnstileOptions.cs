using System.ComponentModel.DataAnnotations;

namespace Malabak.Api.Infrastructure.Configuration;

public sealed class TurnstileOptions
{
    public const string SectionName = "Security:Turnstile";

    public string SecretKey { get; set; } = string.Empty;

    public string SiteKey { get; set; } = string.Empty;

    [Url(ErrorMessage = "VerifyUrl must be a valid URL.")]
    public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public bool BypassInDevelopment { get; set; }
}
