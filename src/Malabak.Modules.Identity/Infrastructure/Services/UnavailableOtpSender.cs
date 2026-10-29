using Malabak.Modules.Identity.Application.Common;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Infrastructure.Services;

/// <summary>Used when no delivery channel is configured: every send fails, so OTP flows fail closed.</summary>
public sealed class UnavailableOtpSender : IOtpSender
{
    private readonly ILogger<UnavailableOtpSender> _logger;

    public UnavailableOtpSender(ILogger<UnavailableOtpSender> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        TimeSpan validity,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "No OTP delivery channel is configured ({Section}:Provider is None); {Purpose} code was not sent.",
            OtpDeliveryOptions.SectionName,
            purpose);

        return Task.FromResult(false);
    }
}
