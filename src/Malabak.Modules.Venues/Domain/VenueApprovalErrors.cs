using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public static class VenueApprovalErrors
{
    public static readonly Error MessageRequired = new("VenueApproval.MessageRequired", "A reason or message is required for this decision.");
    public static readonly Error MessageTooLong = new("VenueApproval.MessageTooLong", "Message must be at most 2000 characters.");
    public static readonly Error NoPitches = new("VenueApproval.NoPitches", "Add at least one pitch before submitting the venue for approval.");

    public static Error InvalidTransition(VenueApprovalStatus current, string action) =>
        new("VenueApproval.Conflict", $"Cannot {action} a venue whose approval status is {current}.");
}
