namespace Malabak.Modules.Identity.Domain;

public enum PhoneChangeStatus
{
    Started = 1,
    OldVerified = 2,
    Completed = 3,
    Expired = 4,
    Failed = 5,
    Cancelled = 6
}
