using FluentValidation;

namespace Malabak.Modules.Matches.Application.Challenges.LinkBooking;

public sealed class LinkChallengeBookingCommandValidator : AbstractValidator<LinkChallengeBookingCommand>
{
    public LinkChallengeBookingCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty();

        RuleFor(x => x.TeamId)
            .NotEmpty();

        RuleFor(x => x.CallerPlayerId)
            .NotEmpty();

        RuleFor(x => x.BookingId)
            .NotEmpty();
    }
}
