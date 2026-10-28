using FluentValidation;

namespace Malabak.Modules.Venues.Application.Schedules.UpdateVenueSchedules;

public sealed class UpdateVenueSchedulesValidator : AbstractValidator<UpdateVenueSchedulesCommand>
{
    public UpdateVenueSchedulesValidator()
    {
        RuleFor(x => x.VenueId)
            .NotEmpty().WithMessage("VenueId is required.");

        RuleFor(x => x.OwnerId)
            .NotEmpty().WithMessage("OwnerId is required.");

        RuleFor(x => x.Schedules)
            .NotEmpty().WithMessage("At least one schedule must be provided.")
            .Must(schedules => schedules.Select(s => s.DayOfWeek).Distinct().Count() == schedules.Count)
            .WithMessage("Each day of the week can only have one schedule entry.");

        RuleForEach(x => x.Schedules).ChildRules(item =>
        {
            item.RuleFor(s => s.SlotDurationMinutes)
                .InclusiveBetween(15, 360)
                .WithMessage("Slot duration must be between 15 and 360 minutes.");

            item.RuleFor(s => s)
                .Must(s => s.OpenTime != s.CloseTime)
                .WithMessage("Open time and close time cannot be equal unless explicitly closed.");
        });
    }
}
