using FluentValidation;

namespace Malabak.Modules.Venues.Application.BlockedSlots.CreateBlockedSlot;

public sealed class CreateBlockedSlotValidator : AbstractValidator<CreateBlockedSlotCommand>
{
    public CreateBlockedSlotValidator()
    {
        RuleFor(x => x.VenueId)
            .NotEmpty().WithMessage("VenueId is required.");

        RuleFor(x => x.PitchId)
            .NotEmpty().WithMessage("PitchId is required.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.StartUtc)
            .NotEmpty().WithMessage("StartUtc is required.");

        RuleFor(x => x.EndUtc)
            .NotEmpty().WithMessage("EndUtc is required.")
            .GreaterThan(x => x.StartUtc).WithMessage("EndUtc must be after StartUtc.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters.");
    }
}
