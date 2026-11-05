using FluentValidation;
using Malabak.Modules.Matches.Domain;

namespace Malabak.Modules.Matches.Application.Matches.ScheduleMatch;

public sealed class ScheduleMatchCommandValidator : AbstractValidator<ScheduleMatchCommand>
{
    public ScheduleMatchCommandValidator()
    {
        RuleFor(x => x.HomeTeamId)
            .NotEmpty()
            .WithMessage("Home team identifier is required.");

        RuleFor(x => x.AwayTeamId)
            .NotEmpty()
            .WithMessage("Away team identifier is required.")
            .NotEqual(x => x.HomeTeamId)
            .WithMessage("Home and away teams cannot be the same.");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(Match.MinDurationMinutes, Match.MaxDurationMinutes)
            .WithMessage($"Duration must be between {Match.MinDurationMinutes} and {Match.MaxDurationMinutes} minutes.");

        RuleFor(x => x.VenueName)
            .MaximumLength(200)
            .When(x => !string.IsNullOrEmpty(x.VenueName));

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrEmpty(x.Notes));
    }
}
