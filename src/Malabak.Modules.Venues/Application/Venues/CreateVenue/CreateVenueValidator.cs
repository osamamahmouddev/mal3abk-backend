using FluentValidation;

namespace Malabak.Modules.Venues.Application.Venues.CreateVenue;

public sealed class CreateVenueValidator : AbstractValidator<CreateVenueCommand>
{
    public CreateVenueValidator()
    {
        RuleFor(x => x.OwnerId)
            .NotEmpty()
            .WithMessage("Owner ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(150)
            .WithMessage("Venue name is required and cannot exceed 150 characters.");

        RuleFor(x => x.Address)
            .NotEmpty()
            .MaximumLength(250)
            .WithMessage("Address is required and cannot exceed 250 characters.");

        RuleFor(x => x.City)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("City is required.");

        RuleFor(x => x.Area)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("Area is required.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Contact phone number is required.");

        When(x => x.Latitude.HasValue, () =>
        {
            RuleFor(x => x.Latitude!.Value)
                .InclusiveBetween(-90, 90)
                .WithMessage("Latitude must be between -90 and 90 degrees.");
        });

        When(x => x.Longitude.HasValue, () =>
        {
            RuleFor(x => x.Longitude!.Value)
                .InclusiveBetween(-180, 180)
                .WithMessage("Longitude must be between -180 and 180 degrees.");
        });
    }
}
