using FluentValidation;
using Malabak.Modules.Venues.Domain;

namespace Malabak.Modules.Venues.Application.Pitches.UpdatePitch;

public sealed class UpdatePitchValidator : AbstractValidator<UpdatePitchCommand>
{
    public UpdatePitchValidator()
    {
        RuleFor(x => x.PitchId)
            .NotEmpty()
            .WithMessage("Pitch ID is required.");

        RuleFor(x => x.OwnerId)
            .NotEmpty()
            .WithMessage("Owner ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("Pitch name is required.");

        RuleFor(x => x.TurfType)
            .NotEmpty()
            .Must(t => SurfaceType.Create(t).IsSuccess)
            .WithMessage("Invalid surface / turf type.");

        RuleFor(x => x.Capacity)
            .Must(c => (c >= 3 && c <= 11) || (c >= 6 && c <= 22))
            .WithMessage("Capacity must be between 3 and 11 players per team (or 6 to 22 total players).");

        When(x => x.LengthMeters.HasValue, () =>
        {
            RuleFor(x => x.LengthMeters!.Value)
                .GreaterThan(0)
                .WithMessage("Length must be greater than zero.");
        });

        When(x => x.WidthMeters.HasValue, () =>
        {
            RuleFor(x => x.WidthMeters!.Value)
                .GreaterThan(0)
                .WithMessage("Width must be greater than zero.");
        });
    }
}
