using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Application.PhoneChange;

public static class PhoneChangeErrors
{
    /// <summary>Generic on purpose: the same answer for "taken", "lost a race" and other unusable numbers (no enumeration).</summary>
    public static readonly Error UnableToUsePhoneNumber = new(
        "Identity.UnableToUsePhoneNumber",
        "This phone number cannot be used.");

    public static readonly Error MaxAttemptsExceeded = new(
        "Identity.PhoneChangeMaxAttemptsExceeded",
        "Too many incorrect codes. Please start a new phone change request.");
}
