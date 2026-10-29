using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.Common;

public static class OtpDeliveryErrors
{
    public const string DeliveryFailedCode = "Identity.OtpDeliveryFailed";

    public static readonly Error DeliveryFailed = new(
        DeliveryFailedCode,
        "The verification code could not be sent. Please try again shortly.");
}
