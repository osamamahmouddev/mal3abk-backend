namespace Malabak.Api.Infrastructure.Security;

/// <summary>An ASP.NET Data Protection key element, persisted so every instance shares the key ring across deploys.</summary>
public sealed class DataProtectionKeyRecord
{
    public int Id { get; set; }
    public string FriendlyName { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
