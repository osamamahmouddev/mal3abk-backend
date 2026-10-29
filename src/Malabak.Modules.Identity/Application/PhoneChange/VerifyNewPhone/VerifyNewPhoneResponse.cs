namespace Malabak.Modules.Identity.Application.PhoneChange.VerifyNewPhone;

/// <summary>
/// No recovery token here: it goes only to the old phone, in the security alert. Returning it to the caller
/// would hand it to the very session it exists to undo (SECURITY.md §5.5).
/// </summary>
public sealed record VerifyNewPhoneResponse(
    bool Success,
    string NewPhoneNumber);
