using FluentValidation;

namespace Malabak.Modules.Matches.Application.Matches.SubmitResult;

public sealed class SubmitMatchResultCommandValidator : AbstractValidator<SubmitMatchResultCommand>
{
    public SubmitMatchResultCommandValidator()
    {
        RuleFor(x => x.MatchId)
            .NotEmpty()
            .WithMessage("Match identifier is required.");

        RuleFor(x => x.SubmittingTeamId)
            .NotEmpty()
            .WithMessage("Submitting team identifier is required.");

        RuleFor(x => x.HomeScore)
            .InclusiveBetween(0, 99)
            .WithMessage("Home score must be between 0 and 99.");

        RuleFor(x => x.AwayScore)
            .InclusiveBetween(0, 99)
            .WithMessage("Away score must be between 0 and 99.");
    }
}
