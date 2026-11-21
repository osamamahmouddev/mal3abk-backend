using Malabak.Modules.Identity.Application.Common;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Delivers OTP codes directly via Meta WhatsApp Business Cloud API.
/// </summary>
public sealed class MetaWhatsAppOtpSender : IOtpSender
{
    private readonly MetaWhatsAppClient _client;

    public MetaWhatsAppOtpSender(MetaWhatsAppClient client)
    {
        _client = client;
    }

    public Task<bool> SendAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        TimeSpan validity,
        CancellationToken cancellationToken = default) =>
        _client.SendOtpAsync(phoneNumber, code, purpose, validity, cancellationToken);
}
