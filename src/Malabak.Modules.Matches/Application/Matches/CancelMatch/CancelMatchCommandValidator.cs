using FluentValidation;

namespace Malabak.Modules.Matches.Application.Matches.CancelMatch;

public sealed class CancelMatchCommandValidator : AbstractValidator<CancelMatchCommand>
{
    public CancelMatchCommandValidator()
    {
        RuleFor(x => x.MatchId)
            .NotEmpty()
            .WithMessage("Match identifier is required.");

        RuleFor(x => x.CallerTeamId)
            .NotEmpty()
            .WithMessage("Caller team identifier is required.");

        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .When(x => !string.IsNullOrEmpty(x.Reason));
    }
}
