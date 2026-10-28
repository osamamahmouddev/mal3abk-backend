using FluentValidation;

namespace Malabak.Modules.Identity.Application.Authentication.SendOtp;

public sealed class SendOtpValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required.")
            .MaximumLength(32).WithMessage("Phone number cannot exceed 32 characters.");

        RuleFor(x => x.TurnstileToken)
            .NotEmpty().WithMessage("Turnstile anti-bot token is required.");
    }
}
