using FluentValidation;

namespace Malabak.Modules.Identity.Application.PhoneChange.VerifyOldPhone;

public sealed class VerifyOldPhoneValidator : AbstractValidator<VerifyOldPhoneCommand>
{
    public VerifyOldPhoneValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");

        RuleFor(x => x.RequestId)
            .NotEmpty()
            .WithMessage("Request ID is required.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .Length(6)
            .Matches("^[0-9]+$")
            .WithMessage("Verification code must be 6 digits.");
    }
}
