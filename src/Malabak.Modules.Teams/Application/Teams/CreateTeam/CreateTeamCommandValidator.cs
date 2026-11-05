using FluentValidation;

namespace Malabak.Modules.Teams.Application.Teams.CreateTeam;

public sealed class CreateTeamCommandValidator : AbstractValidator<CreateTeamCommand>
{
    public CreateTeamCommandValidator()
    {
        RuleFor(x => x.CaptainId)
            .NotEmpty()
            .WithMessage("CaptainId is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Team name is required.")
            .MinimumLength(2)
            .WithMessage("Team name must be at least 2 characters.")
            .MaximumLength(100)
            .WithMessage("Team name must not exceed 100 characters.");

        RuleFor(x => x.City)
            .NotEmpty()
            .WithMessage("City is required.")
            .MaximumLength(100)
            .WithMessage("City must not exceed 100 characters.");

        RuleFor(x => x.Governorate)
            .NotEmpty()
            .WithMessage("Governorate is required.")
            .MaximumLength(100)
            .WithMessage("Governorate must not exceed 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => !string.IsNullOrEmpty(x.Description))
            .WithMessage("Description must not exceed 500 characters.");

        RuleFor(x => x.District)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.District))
            .WithMessage("District must not exceed 100 characters.");

        RuleFor(x => x.LogoUrl)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrEmpty(x.LogoUrl))
            .WithMessage("Logo URL must not exceed 1000 characters.");

        RuleFor(x => x.CoverUrl)
            .MaximumLength(1000)
            .When(x => !string.IsNullOrEmpty(x.CoverUrl))
            .WithMessage("Cover URL must not exceed 1000 characters.");
    }
}
