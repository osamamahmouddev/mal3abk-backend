using Malabak.Modules.Identity.Application.Common;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Sends OTP verification codes directly to the user's WhatsApp using CodeFiye WhatsApp API.
/// </summary>
public sealed class WhatsAppOtpSender : IOtpSender
{
    private readonly CodeFiyeClient _client;

    public WhatsAppOtpSender(CodeFiyeClient client)
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
