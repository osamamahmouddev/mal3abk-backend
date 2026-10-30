namespace Malabak.Modules.Identity.Application.Common;

public sealed class StepUpOptions
{
    public const string SectionName = "Security:StepUp";

    /// <summary>HMAC-SHA256 key for step-up tokens. Required outside Development/Testing.</summary>
    public string SecretKey { get; set; } = string.Empty;
}
