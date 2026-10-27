using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel;
using PhoneNumbers;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class PhoneNumberNormalizer : IPhoneNumberNormalizer
{
    private static readonly PhoneNumberUtil _util = PhoneNumberUtil.GetInstance();

    public Result<string> Normalize(string rawPhoneNumber)
    {
        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
        {
            return Result.Failure<string>(new Error("Identity.InvalidPhoneNumber", "Phone number cannot be empty."));
        }

        try
        {
            var parsed = _util.Parse(rawPhoneNumber, "EG");

            if (!_util.IsValidNumberForRegion(parsed, "EG"))
            {
                return Result.Failure<string>(new Error("Identity.InvalidEgyptianPhoneNumber", "The provided phone number is not a valid Egyptian number."));
            }

            var numberType = _util.GetNumberType(parsed);
            if (numberType != PhoneNumberType.MOBILE && numberType != PhoneNumberType.FIXED_LINE_OR_MOBILE)
            {
                return Result.Failure<string>(new Error("Identity.NotMobileNumber", "Only Egyptian mobile numbers (+20 1x) are accepted for OTP authentication."));
            }

            var formatted = _util.Format(parsed, PhoneNumberFormat.E164);
            return Result.Success(formatted);
        }
        catch (NumberParseException)
        {
            return Result.Failure<string>(new Error("Identity.PhoneNumberParseException", "Failed to parse phone number. Ensure it contains a valid format."));
        }
    }
}
