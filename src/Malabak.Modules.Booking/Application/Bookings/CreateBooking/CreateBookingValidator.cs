using FluentValidation;

namespace Malabak.Modules.Booking.Application.Bookings.CreateBooking;

public sealed class CreateBookingValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingValidator()
    {
        RuleFor(x => x.PitchId).NotEmpty();
        RuleFor(x => x.PlayerId).NotEmpty();
        RuleFor(x => x.StartUtc).LessThan(x => x.EndUtc).WithMessage("Start time must precede end time.");
        RuleFor(x => x.QuoteToken).NotEmpty().WithMessage("Quote token is required.");
        RuleFor(x => x.ExpectedPrice).GreaterThan(0).WithMessage("Expected price must be greater than zero.");
        RuleFor(x => x.ActorUserId).NotEmpty();
    }
}
