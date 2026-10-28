namespace Malabak.SharedKernel;

public static class PhoneNumberErrors
{
    public static readonly Error Empty = Error.Validation("PhoneNumber.Empty", "Phone number is required.");
    public static readonly Error InvalidFormat = Error.Validation("PhoneNumber.InvalidFormat", "Phone number must be a valid E.164 formatted number.");
    public static readonly Error InvalidEgyptianNumber = Error.Validation("PhoneNumber.InvalidEgyptianNumber", "Invalid Egyptian mobile number. Must start with 010, 011, 012, or 015 and be 11 digits.");
}
