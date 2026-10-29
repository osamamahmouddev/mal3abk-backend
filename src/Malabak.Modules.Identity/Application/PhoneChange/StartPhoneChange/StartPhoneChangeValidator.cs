using FluentValidation;

namespace Malabak.Modules.Identity.Application.PhoneChange.StartPhoneChange;

public sealed class StartPhoneChangeValidator : AbstractValidator<StartPhoneChangeCommand>
{
    public StartPhoneChangeValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");

        RuleFor(x => x.NewPhoneNumber)
            .NotEmpty()
            .WithMessage("New phone number is required.");

        RuleFor(x => x.StepUpToken)
            .NotEmpty()
            .WithMessage("Step-up token is required.");
    }
}
