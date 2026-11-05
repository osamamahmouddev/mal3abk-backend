using FluentValidation;

namespace Malabak.Modules.Teams.Application.Teams.Queries.DiscoverTeams;

public sealed class DiscoverTeamsQueryValidator : AbstractValidator<DiscoverTeamsQuery>
{
    public DiscoverTeamsQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50)
            .WithMessage("PageSize must be between 1 and 50.");

        RuleFor(x => x.Search)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.Search))
            .WithMessage("Search query must not exceed 100 characters.");

        RuleFor(x => x.MinRating)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinRating.HasValue)
            .WithMessage("MinRating cannot be negative.");

        RuleFor(x => x.MaxRating)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MaxRating.HasValue)
            .WithMessage("MaxRating cannot be negative.");

        RuleFor(x => x)
            .Must(x => !x.MinRating.HasValue || !x.MaxRating.HasValue || x.MinRating <= x.MaxRating)
            .WithMessage("MinRating must be less than or equal to MaxRating.");
    }
}
