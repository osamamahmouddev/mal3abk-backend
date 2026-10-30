namespace Malabak.Modules.Identity.Application.Common;

/// <summary>
/// Masks phone numbers before they reach logs or chat channels (SECURITY.md §15.5).
/// </summary>
public static class PhoneNumberMasker
{
    /// <summary>Keeps the country and operator prefix and the last three digits: +201012345678 → +2010•••••678.</summary>
    public static string Mask(string phoneNumber)
    {
        const int visiblePrefix = 5;
        const int visibleSuffix = 3;

        if (string.IsNullOrEmpty(phoneNumber) || phoneNumber.Length <= visiblePrefix + visibleSuffix)
        {
            return new string('•', phoneNumber?.Length ?? 0);
        }

        var hidden = phoneNumber.Length - visiblePrefix - visibleSuffix;
        return string.Concat(phoneNumber.AsSpan(0, visiblePrefix), new string('•', hidden), phoneNumber.AsSpan(phoneNumber.Length - visibleSuffix));
    }
}
