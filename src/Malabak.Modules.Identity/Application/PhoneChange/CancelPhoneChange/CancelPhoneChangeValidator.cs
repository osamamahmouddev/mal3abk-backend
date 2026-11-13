using FluentValidation;

namespace Malabak.Modules.Identity.Application.PhoneChange.CancelPhoneChange;

public sealed class CancelPhoneChangeValidator : AbstractValidator<CancelPhoneChangeCommand>
{
    public CancelPhoneChangeValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");

        RuleFor(x => x.RequestId)
            .NotEmpty()
            .WithMessage("Request ID is required.");
    }
}
