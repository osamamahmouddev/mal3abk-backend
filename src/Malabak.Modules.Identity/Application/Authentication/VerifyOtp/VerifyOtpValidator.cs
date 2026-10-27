using FluentValidation;

namespace Malabak.Modules.Identity.Application.Authentication.VerifyOtp;

public sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required.")
            .Length(6).WithMessage("Verification code must be exactly 6 digits.")
            .Matches("^[0-9]{6}$").WithMessage("Verification code must contain digits only.");
    }
}
