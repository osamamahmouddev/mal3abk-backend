namespace Malabak.Api.Infrastructure.Security;

public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    public bool Enabled { get; set; } = true;
    public bool TrustLoopbackInDevelopment { get; set; } = true;
    public List<string> AdditionalTrustedProxies { get; set; } = [];
}
