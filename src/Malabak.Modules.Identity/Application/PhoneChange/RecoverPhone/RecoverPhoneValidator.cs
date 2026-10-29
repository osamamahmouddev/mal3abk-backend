using FluentValidation;

namespace Malabak.Modules.Identity.Application.PhoneChange.RecoverPhone;

public sealed class RecoverPhoneValidator : AbstractValidator<RecoverPhoneCommand>
{
    public RecoverPhoneValidator()
    {
        RuleFor(x => x.RecoveryToken)
            .NotEmpty()
            .WithMessage("Recovery token is required.");
    }
}
