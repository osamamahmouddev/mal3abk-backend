using FluentValidation;
using Malabak.Modules.Matches.Domain;

namespace Malabak.Modules.Matches.Application.Challenges.SendChallenge;

public sealed class SendChallengeCommandValidator : AbstractValidator<SendChallengeCommand>
{
    public SendChallengeCommandValidator()
    {
        RuleFor(x => x.ChallengerTeamId)
            .NotEmpty();

        RuleFor(x => x.ChallengedTeamId)
            .NotEmpty()
            .NotEqual(x => x.ChallengerTeamId)
            .WithMessage("A team cannot challenge itself.");

        RuleFor(x => x.CallerPlayerId)
            .NotEmpty();

        RuleFor(x => x.ProposedDateUtc)
            .NotEmpty();

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(TeamChallenge.MinDurationMinutes, TeamChallenge.MaxDurationMinutes);

        RuleFor(x => x.ProposedVenueName)
            .MaximumLength(200)
            .When(x => !string.IsNullOrEmpty(x.ProposedVenueName));

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
