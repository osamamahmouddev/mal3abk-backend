using FluentValidation;
using Malabak.Modules.Matches.Domain;

namespace Malabak.Modules.Matches.Application.Matches.ResolveDispute;

public sealed class ResolveMatchDisputeCommandValidator : AbstractValidator<ResolveMatchDisputeCommand>
{
    public ResolveMatchDisputeCommandValidator()
    {
        RuleFor(x => x.MatchId)
            .NotEmpty()
            .WithMessage("Match ID is required.");

        RuleFor(x => x.AdminUserId)
            .NotEmpty()
            .WithMessage("Admin user ID is required.");

        RuleFor(x => x.ResolutionNotes)
            .NotEmpty()
            .MaximumLength(1000)
            .WithMessage("Resolution notes are required and must not exceed 1000 characters.");

        RuleFor(x => x.Action)
            .IsInEnum()
            .WithMessage("Valid resolution action is required.");

        When(x => x.Action == MatchDisputeResolutionAction.SetOfficialScore, () =>
        {
            RuleFor(x => x.OfficialHomeScore)
                .NotNull()
                .InclusiveBetween(0, 99)
                .WithMessage("Official home score must be between 0 and 99.");

            RuleFor(x => x.OfficialAwayScore)
                .NotNull()
                .InclusiveBetween(0, 99)
                .WithMessage("Official away score must be between 0 and 99.");
        });
    }
}
