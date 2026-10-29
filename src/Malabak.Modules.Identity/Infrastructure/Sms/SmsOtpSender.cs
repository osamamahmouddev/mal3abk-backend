using Malabak.Modules.Identity.Application.Common;

namespace Malabak.Modules.Identity.Infrastructure.Sms;

/// <summary>
/// Delivers one-time codes through the cost-controlled <see cref="ISmsGateway"/> (country allow-list, spend caps,
/// provider failover). A blocked or failed send returns false so the OTP flow fails closed; the gateway never
/// logs the code or the full number.
/// </summary>
public sealed class SmsOtpSender : IOtpSender
{
    private readonly ISmsGateway _gateway;

    public SmsOtpSender(ISmsGateway gateway)
    {
        _gateway = gateway;
    }

    public async Task<bool> SendAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        TimeSpan validity,
        CancellationToken cancellationToken = default) =>
        await _gateway.SendOtpAsync(phoneNumber, code, cancellationToken) == SmsSendOutcome.Sent;
}
