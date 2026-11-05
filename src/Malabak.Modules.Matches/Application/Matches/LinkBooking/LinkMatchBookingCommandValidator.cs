using FluentValidation;

namespace Malabak.Modules.Matches.Application.Matches.LinkBooking;

public sealed class LinkMatchBookingCommandValidator : AbstractValidator<LinkMatchBookingCommand>
{
    public LinkMatchBookingCommandValidator()
    {
        RuleFor(x => x.MatchId)
            .NotEmpty()
            .WithMessage("Match identifier is required.");

        RuleFor(x => x.CallerTeamId)
            .NotEmpty()
            .WithMessage("Caller team identifier is required.");

        RuleFor(x => x.BookingId)
            .NotEmpty()
            .WithMessage("Booking identifier is required.");
    }
}
