namespace Malabak.SharedKernel;

public static class TimeSlotRangeErrors
{
    public static readonly Error InvalidRange = Error.Validation(
        "TimeSlotRange.InvalidRange",
        "Slot start time must precede end time.");

    public static readonly Error ZeroOrNegativeDuration = Error.Validation(
        "TimeSlotRange.ZeroOrNegativeDuration",
        "Slot duration must be strictly positive.");

    public static readonly Error ExceedsMaxDuration = Error.Validation(
        "TimeSlotRange.ExceedsMaxDuration",
        "A single slot range cannot exceed 24 hours.");
}
