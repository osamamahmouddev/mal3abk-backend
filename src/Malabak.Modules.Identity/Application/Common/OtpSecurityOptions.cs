namespace Malabak.Modules.Identity.Application.Common;

public sealed class OtpSecurityOptions
{
    public const string SectionName = "Security:Otp";

    /// <summary>HMAC-SHA256 key for stored OTP hashes. Required outside Development/Testing.</summary>
    public string HmacSecret { get; set; } = string.Empty;
}
