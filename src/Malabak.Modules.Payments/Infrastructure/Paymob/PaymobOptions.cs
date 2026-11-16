namespace Malabak.Modules.Payments.Infrastructure.Paymob;

public sealed class PaymobOptions
{
    public const string SectionName = "Paymob";

    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string HmacSecret { get; set; } = string.Empty;
    public int CardIntegrationId { get; set; }
    public int WalletIntegrationId { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
}
