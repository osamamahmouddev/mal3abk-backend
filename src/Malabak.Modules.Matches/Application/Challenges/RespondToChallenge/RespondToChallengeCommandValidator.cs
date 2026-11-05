using FluentValidation;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;

namespace Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;

public sealed class RespondToChallengeCommandValidator : AbstractValidator<RespondToChallengeCommand>
{
    public RespondToChallengeCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty();

        RuleFor(x => x.RespondingTeamId)
            .NotEmpty();

        RuleFor(x => x.CallerPlayerId)
            .NotEmpty();

        RuleFor(x => x.Action)
            .IsInEnum();

        RuleFor(x => x.NewProposedDateUtc)
            .NotEmpty()
            .When(x => x.Action == ChallengeResponseAction.CounterPropose);

        RuleFor(x => x.NewDurationMinutes)
            .InclusiveBetween(TeamChallenge.MinDurationMinutes, TeamChallenge.MaxDurationMinutes)
            .When(x => x.Action == ChallengeResponseAction.CounterPropose && x.NewDurationMinutes.HasValue);

        RuleFor(x => x.DeclineReason)
            .MaximumLength(500)
            .When(x => x.Action == ChallengeResponseAction.Decline);
    }
}
