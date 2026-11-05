using FluentValidation;
using Malabak.Modules.Matches.Domain;

namespace Malabak.Modules.Matches.Application.Matches.VerifyResult;

public sealed class VerifyMatchResultCommandValidator : AbstractValidator<VerifyMatchResultCommand>
{
    public VerifyMatchResultCommandValidator()
    {
        RuleFor(x => x.MatchId)
            .NotEmpty()
            .WithMessage("Match identifier is required.");

        RuleFor(x => x.VerifyingTeamId)
            .NotEmpty()
            .WithMessage("Verifying team identifier is required.");

        RuleFor(x => x.Action)
            .IsInEnum()
            .WithMessage("A valid verification action (Confirm or Dispute) is required.");

        RuleFor(x => x.DisputeReason)
            .NotEmpty()
            .When(x => x.Action == MatchVerificationAction.Dispute)
            .WithMessage("A dispute reason is required when disputing a match result.");

        RuleFor(x => x.DisputeReason)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrEmpty(x.DisputeReason));
    }
}
