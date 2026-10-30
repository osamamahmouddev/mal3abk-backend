using System.ComponentModel.DataAnnotations;

namespace Malabak.Modules.Identity.Application.Common;

public sealed class TurnstileOptions
{
    public const string SectionName = "Security:Turnstile";

    public string SecretKey { get; set; } = string.Empty;

    public string SiteKey { get; set; } = string.Empty;

    [Url(ErrorMessage = "VerifyUrl must be a valid URL.")]
    public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    /// <summary>Skips the Cloudflare call. Honoured only in Development/Testing; refused at startup elsewhere.</summary>
    public bool BypassInDevelopment { get; set; }
}
